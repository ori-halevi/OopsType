using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Threading;
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

    private readonly DispatcherTimer _pollTimer;
    private WinEventHook? _focusHook;
    private LanguageInfo _current = LanguageInfo.Unknown;

    public LanguageInfo Current => _current;
    public event Action<LanguageInfo>? LanguageChanged;

    public KeyboardLayoutService()
    {
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
        _pollTimer.Tick += (_, _) => CheckLayout();
    }

    public void Start()
    {
        _focusHook = new WinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_OBJECT_FOCUS);
        _focusHook.EventRaised += (_, _) => CheckLayout();
        _pollTimer.Start();
        CheckLayout();
    }

    public IReadOnlyList<LanguageInfo> GetInstalledLayouts()
    {
        var count = NativeGetKeyboardLayoutList(0, null);
        if (count <= 0) return Array.Empty<LanguageInfo>();

        var handles = new IntPtr[count];
        NativeGetKeyboardLayoutList(count, handles);

        var seen = new HashSet<int>();
        var list = new List<LanguageInfo>(count);
        foreach (var hkl in handles)
        {
            var info = LocaleResolver.Resolve(hkl);
            // De-dupe variants of the same language (e.g. en-US vs en-GB both map to "EN").
            if (seen.Add(info.LangId)) list.Add(info);
        }
        return list;
    }

    public bool RequestLanguage(string twoLetterCode)
    {
        if (string.IsNullOrWhiteSpace(twoLetterCode)) return false;

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
        NativeMethods.PostMessage(hwnd, NativeMethods.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target);
        return true;
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
        LanguageChanged?.Invoke(info);
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
