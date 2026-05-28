using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using OopsType.Infrastructure;
using OopsType.Models;
using OopsType.Native;

namespace OopsType.Services;

/// <summary>
/// Tracks the keyboard layout of the foreground window. Uses two complementary signals:
///   - <c>WinEvent</c> hook fires on foreground/focus changes (fast, immediate).
///   - A short-interval poll catches in-app HKL flips (Win+Space, Alt+Shift) that don't raise
///     focus events. 80 ms is small enough to feel instantaneous to the user.
/// Pure locale lookup logic lives in <see cref="LocaleResolver"/>; this class only orchestrates
/// detection and change notification.
/// </summary>
public sealed class KeyboardLayoutService : IKeyboardLayoutService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(80);

    private readonly IErrorReporter _reporter;
    private readonly DispatcherTimer _pollTimer;
    private WinEventHook? _focusHook;
    private LanguageInfo _current = LanguageInfo.Unknown;

    public LanguageInfo Current => _current;
    public event Action<LanguageInfo>? LanguageChanged;

    public KeyboardLayoutService(IErrorReporter reporter)
    {
        _reporter = reporter;
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
        _pollTimer.Tick += (_, _) => Safe.Invoke(_reporter, "KeyboardLayoutService.PollTick", CheckLayout);
    }

    public void Start()
    {
        _focusHook = new WinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_OBJECT_FOCUS, _reporter);
        _focusHook.EventRaised += (_, _) => Safe.Invoke(_reporter, "KeyboardLayoutService.FocusEvent", CheckLayout);
        _pollTimer.Start();
        Safe.Invoke(_reporter, "KeyboardLayoutService.InitialCheck", CheckLayout);
    }

    public IReadOnlyList<LanguageInfo> GetInstalledLayouts()
    {
        try
        {
            var count = NativeGetKeyboardLayoutList(0, null);
            if (count <= 0) return Array.Empty<LanguageInfo>();

            var handles = new IntPtr[count];
            // The second call's return value is the actual number written — it can differ from
            // count if the user installed/uninstalled a layout between the two calls. Iterate
            // only what was actually written to avoid surfacing stale handles past the end.
            var written = NativeGetKeyboardLayoutList(count, handles);
            if (written <= 0) return Array.Empty<LanguageInfo>();
            var actual = Math.Min(written, count);

            var seen = new HashSet<int>();
            var list = new List<LanguageInfo>(actual);
            for (int i = 0; i < actual; i++)
            {
                var info = LocaleResolver.Resolve(handles[i]);
                // De-dupe variants of the same language (e.g. en-US vs en-GB both map to "EN").
                if (seen.Add(info.LangId)) list.Add(info);
            }
            return list;
        }
        catch (Exception ex)
        {
            _reporter.Report("KeyboardLayoutService.GetInstalledLayouts", ex);
            return Array.Empty<LanguageInfo>();
        }
    }

    public bool RequestLanguage(string twoLetterCode)
    {
        if (string.IsNullOrWhiteSpace(twoLetterCode)) return false;

        try
        {
            var target = IntPtr.Zero;
            foreach (var l in GetInstalledLayouts())
            {
                if (string.Equals(l.TwoLetterCode, twoLetterCode, StringComparison.OrdinalIgnoreCase))
                {
                    target = l.Hkl;
                    break;
                }
            }
            if (target == IntPtr.Zero) return false;

            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            // wParam = 0 means "use the HKL passed in lParam directly" (not INPUTLANGCHANGE_FORWARD,
            // which would ignore lParam and just step through installed layouts).
            return NativeMethods.PostMessage(hwnd, NativeMethods.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target);
        }
        catch (Exception ex)
        {
            _reporter.Report("KeyboardLayoutService.RequestLanguage", ex);
            return false;
        }
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _focusHook?.Dispose();
        _focusHook = null;
    }

    private void CheckLayout()
    {
        var info = GetForegroundLayout();
        if (info.Hkl == _current.Hkl) return;

        _current = info;
        // A buggy LanguageChanged subscriber must not destabilize the poll loop.
        try { LanguageChanged?.Invoke(info); }
        catch (Exception ex) { _reporter.Report("KeyboardLayoutService.LanguageChanged", ex); }
    }

    private static LanguageInfo GetForegroundLayout()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return LanguageInfo.Unknown;
        var tid = NativeMethods.GetWindowThreadProcessId(hwnd, IntPtr.Zero);
        var hkl = NativeMethods.GetKeyboardLayout(tid);
        return LocaleResolver.Resolve(hkl);
    }

    [DllImport("user32.dll", EntryPoint = "GetKeyboardLayoutList")]
    private static extern int NativeGetKeyboardLayoutList(int nBuff, IntPtr[]? lpList);
}
