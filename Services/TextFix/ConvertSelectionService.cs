using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Threading;
using OopsType.Infrastructure;
using OopsType.Models;
using OopsType.Native;
using OopsType.Services.Overlays;

namespace OopsType.Services.TextFix;

/// <summary>
/// Orchestrates "switch language with text selected, get the text re-typed correctly".
///
/// <para><b>Trigger.</b> The layout switch itself, not a hotkey. Users change layouts with Alt+Shift,
/// Ctrl+Shift, Win+Space or the language bar depending on how Windows is configured, and hooking
/// any one chord would work for some of them and silently fail for the rest. Listening to
/// <see cref="IKeyboardLayoutService.LanguageChanged"/> covers every route for free.</para>
///
/// <para><b>Threading.</b> Identical shape to <see cref="Overlays.CaretOverlayPresenter"/>, and for
/// the same reason: reading the selection is cross-process UI Automation that can block for seconds
/// against a busy app, and typing waits for the user to release Alt. Both run on a dedicated MTA
/// worker; the UI thread only ever hands over a request and, at the end, positions the feedback chip.
/// </para>
///
/// <para><b>Safety.</b> This is the only part of OopsType that writes into the user's documents, so
/// every step is a veto: too long, more than one script, a read-only target, an irreversible mapping,
/// or the foreground window having changed all abort the whole thing rather than converting
/// approximately. See <see cref="Process"/> for the gate order.</para>
/// </summary>
public sealed class ConvertSelectionService : IConvertSelectionService
{
    // A layout change this much older than "now" is no longer plausibly the switch the user just
    // made — the worker was busy, or the machine stalled. Converting on it would surprise them.
    private static readonly TimeSpan RequestStaleAfter = TimeSpan.FromSeconds(2);

    // A layout change arriving this soon after a foreground change is almost certainly just the new
    // app carrying its own input locale, not a deliberate switch. Converting there would rewrite
    // whatever happened to be selected in the window the user merely alt-tabbed into. Sized to
    // comfortably cover KeyboardLayoutService's own detection latency (an 80ms poll plus a focus
    // hook), so the layout change always lands inside the window it is attributable to.
    private static readonly TimeSpan ForegroundSettleWindow = TimeSpan.FromMilliseconds(600);

    // How often we sample which window is in front. Cheap (one GetForegroundWindow per tick) and
    // frequent enough that a window is credited with holding focus almost from the moment it does.
    private static readonly TimeSpan ForegroundPollInterval = TimeSpan.FromMilliseconds(100);

    // Sentinel for "no foreground observed yet", distinct from IntPtr.Zero (which a real
    // GetForegroundWindow call returns when nothing is focused at all).
    private static readonly IntPtr NoForeground = new(-1);

    // Give the target app a moment to actually process the synthetic keystrokes before asking where
    // the caret ended up, so the chip lands on the converted text rather than its old position.
    private static readonly TimeSpan CaretSettleDelay = TimeSpan.FromMilliseconds(60);

    // Same idea before re-selecting: SendInput only queues the keystrokes, and UI Automation would
    // otherwise read a caret that has not moved yet.
    private static readonly TimeSpan InputSettleDelay = TimeSpan.FromMilliseconds(40);

    private static readonly char[] LineBreaks = { '\r', '\n' };

    private readonly ISettingsService _settings;
    private readonly IKeyboardLayoutService _layout;
    private readonly ICaretLocationService _caret;
    private readonly ConvertChipPresenter _chip;
    private readonly IErrorReporter _reporter;
    private readonly ILogger _logger;

    private DispatcherTimer? _foregroundPoll;
    private Dispatcher? _dispatcher;

    private Thread? _worker;
    private AutoResetEvent? _signal;
    private volatile bool _workerStop;

    private readonly object _pendingLock = new();
    private Request? _pending;

    // Layout seen at the previous notification — the "from" side of the switch. Tracked here rather
    // than asked of the layout service, which only ever publishes the new value.
    private LanguageInfo _previous = LanguageInfo.Unknown;

    // Last foreground window we know about, and when we first saw it. Sampled by a timer and
    // re-read on every layout change — see NoteForeground for why it is a poll and not a hook.
    private IntPtr _knownForeground = NoForeground;
    private long _foregroundSinceTicks;

    private readonly record struct Request(LanguageInfo Previous, LanguageInfo Current, IntPtr Foreground, long Ticks);

    public ConvertSelectionService(
        ISettingsService settings,
        IKeyboardLayoutService layout,
        ICaretLocationService caret,
        ConvertChipPresenter chip,
        IErrorReporter reporter,
        ILogger logger)
    {
        _settings = settings;
        _layout = layout;
        _caret = caret;
        _chip = chip;
        _reporter = reporter;
        _logger = logger;
    }

    public void Start()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _previous = _layout.Current;
        NoteForeground(NativeMethods.GetForegroundWindow());

        _foregroundPoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = ForegroundPollInterval };
        _foregroundPoll.Tick += (_, _) => Safe.Invoke(_reporter, "ConvertSelectionService.ForegroundPoll",
            () => NoteForeground(NativeMethods.GetForegroundWindow()));
        _foregroundPoll.Start();

        _layout.LanguageChanged += OnLanguageChanged;

        StartWorker();
    }

    public void Dispose()
    {
        _layout.LanguageChanged -= OnLanguageChanged;

        _foregroundPoll?.Stop();
        _foregroundPoll = null;

        StopWorker();
        _dispatcher = null;
    }

    /// <summary>
    /// Records which window is in front and, when that differs from what we last saw, when it took
    /// over. Returns how long the CURRENT window has held the foreground.
    ///
    /// <para><b>Why a poll and not a WinEvent hook.</b> Two earlier attempts failed for instructive
    /// reasons. A plain "was there a foreground event recently" timestamp never fired, because
    /// <see cref="KeyboardLayoutService"/> installs its EVENT_SYSTEM_FOREGROUND hook first and so its
    /// layout notification always arrived before our hook could stamp anything. Adding the window's
    /// identity fixed that but broke the opposite case: EVENT_SYSTEM_FOREGROUND also fires for the
    /// shell's transient surfaces, whose handles never match <c>GetForegroundWindow</c> by the time
    /// we look, so every sample disagreed with the next and the clock reset forever — suppressing
    /// deliberate switches too. Sampling <c>GetForegroundWindow</c> ourselves means this check and
    /// <see cref="OnLanguageChanged"/> read the exact same source of truth, and neither hook ordering
    /// nor transient windows can desynchronise them.</para>
    /// </summary>
    private TimeSpan NoteForeground(IntPtr hwnd)
    {
        // Look straight through shell surfaces. Win+Space and the taskbar language indicator both
        // raise their own popup, which owns the foreground for as long as it is open; treating that
        // as a window switch would restart the dwell clock and make every switch made through them
        // fail the settle gate -- i.e. the feature would work with Alt+Shift and silently not with
        // Win+Space. Holding the last real window keeps the clock running underneath the popup.
        if (ShellWindowClasses.IsTransient(hwnd))
            return _knownForeground == NoForeground ? TimeSpan.Zero : Stopwatch.GetElapsedTime(_foregroundSinceTicks);

        if (hwnd != _knownForeground)
        {
            _knownForeground = hwnd;
            _foregroundSinceTicks = Stopwatch.GetTimestamp();
            return TimeSpan.Zero;
        }
        return Stopwatch.GetElapsedTime(_foregroundSinceTicks);
    }

    /// <summary>
    /// Runs on the UI thread from the layout service's poll/focus tick. Does no work beyond deciding
    /// whether this change is worth investigating, then wakes the worker.
    /// </summary>
    private void OnLanguageChanged(LanguageInfo current)
    {
        var previous = _previous;
        _previous = current;

        var rawForeground = NativeMethods.GetForegroundWindow();

        // Note the foreground on EVERY layout change, including ones we go on to ignore, so the
        // "how long has this window been in front" clock stays accurate.
        var heldFor = NoteForeground(rawForeground);

        // While a shell popup is up, the window we would actually type into is the one underneath,
        // which NoteForeground has been holding for us.
        var foreground = ShellWindowClasses.IsTransient(rawForeground) ? _knownForeground : rawForeground;

        if (!_settings.Current.ConvertSelection.Enabled) return;
        if (previous.Hkl == IntPtr.Zero || previous.Hkl == current.Hkl) return;
        if (foreground == IntPtr.Zero || foreground == NoForeground) return;

        // Alt-tabbing between an app typing Hebrew and one typing English raises a layout change
        // too. Suppress those: the user switched windows, not languages, and whatever is selected
        // over there was not typed in anger a moment ago.
        if (heldFor < ForegroundSettleWindow) return;

        // Single slot, latest wins: a user tapping through three layouts should act on the last
        // switch, not queue up three conversions of the same selection.
        lock (_pendingLock)
        {
            _pending = new Request(previous, current, foreground, Stopwatch.GetTimestamp());
        }
        _signal?.Set();
    }

    private void StartWorker()
    {
        _workerStop = false;
        _signal = new AutoResetEvent(false);
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "OopsType.ConvertSelection",
        };
        // UI Automation clients belong on an MTA thread, same as the caret presenter's worker.
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
    }

    private void StopWorker()
    {
        var worker = _worker;
        var signal = _signal;

        _workerStop = true;
        signal?.Set();

        // Same bargain as CaretOverlayPresenter.StopWorker: if the worker is stuck inside a slow UIA
        // call we let it finish on its own rather than disposing the handle out from under it, which
        // would throw on its next WaitOne and take the process down. One leaked handle beats that.
        if (worker != null && worker.Join(TimeSpan.FromSeconds(3)))
        {
            signal?.Dispose();
            _signal = null;
        }

        _worker = null;
    }

    private void WorkerLoop()
    {
        var signal = _signal;
        if (signal == null) return;

        while (true)
        {
            signal.WaitOne();
            if (_workerStop) return;

            Request? request;
            lock (_pendingLock)
            {
                request = _pending;
                _pending = null;
            }
            if (request is not { } req) continue;

            try { Process(req); }
            catch (Exception ex) { _reporter.Report("ConvertSelectionService.Process", ex); }
        }
    }

    /// <summary>
    /// The gate chain, cheapest and most conservative first. Any gate that does not pass means the
    /// user's text is left exactly as it was — there is no partial conversion and no "best effort".
    /// </summary>
    private void Process(Request req)
    {
        var cfg = _settings.Current.ConvertSelection;
        if (!cfg.Enabled) return;

        // The switch we were told about has aged out while we were busy elsewhere.
        if (Stopwatch.GetElapsedTime(req.Ticks) > RequestStaleAfter) return;

        var maxLength = Math.Max(1, cfg.MaxLength);
        var snapshot = SelectionReader.Read(maxLength, req.Foreground, _reporter);
        if (!snapshot.Found) return;

        // Nobody types more than a sentence or two before noticing the wrong layout. The cap is the
        // main protection against a stray Ctrl+A: rewriting a whole document would be the one
        // outcome the user could not casually walk back.
        if (snapshot.OverLimit) return;

        var text = snapshot.Text;
        if (cfg.BlockMultiline && (text.Contains('\n') || text.Contains('\r'))) return;

        // Mixed-script text means part of the selection was typed deliberately. Converting all of it
        // would damage the part that was already right, so we decline the whole thing.
        if (!ScriptClassifier.TrySingleScript(text, out var script)) return;

        var direction = DirectionResolver.Resolve(script, req.Previous, req.Current, _layout.GetInstalledLayouts());
        if (!direction.Resolved) return;

        var converted = LayoutTransposer.Transpose(text, direction.Source.Hkl, direction.Target.Hkl);
        if (string.Equals(converted, text, StringComparison.Ordinal)) return;

        // The input being single-script does not make the OUTPUT single-script. Characters with no
        // counterpart on the target layout pass through untouched, and on a case-less target (Hebrew,
        // Arabic, Thai) that includes every capital letter, because Shift+key there produces nothing
        // at all. "Akuo guko" would otherwise be written back as "A<hebrew>", half-converted -- and
        // the reversibility check below cannot catch it, since an untouched character round-trips
        // perfectly. Rejecting a mixed result is the only gate that sees this.
        if (!ScriptClassifier.TrySingleScript(converted, out _)) return;

        // The feature ships without an undo stack on the promise that selecting the result and
        // switching language again restores the original. Verify that promise holds for this exact
        // text before relying on it.
        if (!LayoutTransposer.IsReversible(text, converted, direction.Source.Hkl, direction.Target.Hkl)) return;

        if (!TextInjector.Replace(converted, req.Foreground, _reporter)) return;

        if (cfg.ReselectAfterConvert) Reselect(converted, req.Foreground);

        _logger.Info($"convert-selection {direction.Source.TwoLetterCode}->{direction.Target.TwoLetterCode} len={text.Length}");

        if (cfg.ShowChip) ShowChip(direction.Target);
    }

    /// <summary>
    /// Leaves the converted text highlighted, so switching language once more converts it back.
    ///
    /// <para>Preferred route is UI Automation, whose ranges count logical characters and therefore
    /// behave the same in Hebrew as in English. Synthetic Shift+Left is only a fallback for providers
    /// that cannot select a range: it depends on whether the control moves the caret logically or
    /// visually, and gets right-to-left text backwards in the latter. It is skipped entirely for text
    /// with line breaks, where the keystroke count cannot be trusted.</para>
    /// </summary>
    private void Reselect(string converted, IntPtr foreground)
    {
        // The keystrokes are queued, not applied — give the app a moment to actually insert the text
        // before asking UI Automation where the caret ended up.
        Thread.Sleep(InputSettleDelay);

        if (SelectionWriter.TrySelectPreceding(converted.Length, foreground, _reporter)) return;

        if (converted.IndexOfAny(LineBreaks) >= 0) return;
        TextInjector.SelectPrecedingWithKeys(converted.Length, _reporter);
    }

    /// <summary>
    /// Resolves the caret position on this worker (another UIA call) and hands the plain-value
    /// result to the UI thread, which does nothing but place and fade the chip.
    /// </summary>
    private void ShowChip(LanguageInfo target)
    {
        Thread.Sleep(CaretSettleDelay);

        CaretInfo caret;
        try { caret = _caret.GetCaretRect(); }
        catch (Exception ex)
        {
            _reporter.Report("ConvertSelectionService.Caret", ex);
            caret = default;
        }

        _dispatcher?.BeginInvoke(new Action(() => _chip.Flash(caret, target)));
    }
}
