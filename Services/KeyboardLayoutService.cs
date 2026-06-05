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

    // Transient shell/system surfaces that briefly steal the foreground but never represent a place
    // the user is typing: the taskbar itself, the Win11 quick-settings (network/sound/battery)
    // flyout, the notification/calendar flyout, task view, and the IME/language switch popup. Each
    // runs on a shell UI thread that carries its OWN input locale, unrelated to the app the user was
    // actually typing in. If we let one drive the indicator, clicking it flips the chip + strip
    // colors and closing it flips them back — a spurious inversion. While one of these holds the
    // foreground we keep showing the last real app's layout instead.
    private static readonly HashSet<string> TransientShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",                        // primary taskbar
        "Shell_SecondaryTrayWnd",               // taskbar on secondary monitors
        "TrayNotifyWnd",                        // notification area / clock region
        "ControlCenterWindow",                  // Win11 quick settings (network / sound / battery)
        "TopLevelWindowForOverflowXamlIsland",  // taskbar corner / system-tray overflow
        "Shell_InputSwitchTopLevelWindow",      // language / input-method switch popup
        "MultitaskingViewFrame",                // task view (Win+Tab)
        "XamlExplorerHostIslandWindow",         // Win11 start / search / widgets host
    };

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

    public void EnsureInstalled()
    {
        // Re-arm the focus hook if the OS dropped it (session lock, RDP reconnect, resume from
        // sleep). The poll timer keeps running regardless, but losing the focus signal means
        // layout flips that don't change foreground would take up to PollInterval to notice.
        _focusHook?.EnsureInstalled();
        Safe.Invoke(_reporter, "KeyboardLayoutService.EnsureInstalled", CheckLayout);
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
        // null = no real input surface in the foreground (lost focus, or a transient shell flyout).
        // Hold the last known layout rather than flipping the indicator to the shell's locale.
        if (info == null || info.Hkl == _current.Hkl) return;

        _current = info;
        // A buggy LanguageChanged subscriber must not destabilize the poll loop.
        try { LanguageChanged?.Invoke(info); }
        catch (Exception ex) { _reporter.Report("KeyboardLayoutService.LanguageChanged", ex); }
    }

    /// <summary>
    /// Resolves the layout of the foreground app's input target, or <c>null</c> when the foreground
    /// is something we should ignore (no window, or a transient shell surface — see
    /// <see cref="TransientShellClasses"/>) so the caller can hold the last real value.
    /// </summary>
    private static LanguageInfo? GetForegroundLayout()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        if (TransientShellClasses.Contains(NativeMethods.GetWindowClass(hwnd)))
            return null;

        var hkl = NativeMethods.GetKeyboardLayout(ResolveInputThread(hwnd));
        return LocaleResolver.Resolve(hkl);
    }

    /// <summary>
    /// Thread whose input locale represents what the user is typing. For classic apps that's simply
    /// the foreground window's own thread, but modern WinUI / island-hosted apps (e.g. the Windows 11
    /// Notepad) route keyboard focus to a child window owned by a DIFFERENT UI thread; reading the
    /// outer frame thread's layout there returns a value that never tracks an in-app language switch.
    /// GUITHREADINFO names the genuinely focused window, so we prefer its thread and fall back to the
    /// foreground thread when focus info is unavailable.
    /// </summary>
    private static uint ResolveInputThread(IntPtr foreground)
    {
        var tid = NativeMethods.GetWindowThreadProcessId(foreground, IntPtr.Zero);

        var info = new NativeMethods.GUITHREADINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
        };
        if (!NativeMethods.GetGUIThreadInfo(tid, ref info))
            return tid;

        var focusWindow = info.hwndFocus != IntPtr.Zero ? info.hwndFocus
                        : info.hwndCaret != IntPtr.Zero ? info.hwndCaret
                        : IntPtr.Zero;
        if (focusWindow == IntPtr.Zero)
            return tid;

        var focusTid = NativeMethods.GetWindowThreadProcessId(focusWindow, IntPtr.Zero);
        return focusTid != 0 ? focusTid : tid;
    }

    [DllImport("user32.dll", EntryPoint = "GetKeyboardLayoutList")]
    private static extern int NativeGetKeyboardLayoutList(int nBuff, IntPtr[]? lpList);
}
