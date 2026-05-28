using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Threading;
using OopsType.Models;
using OopsType.Native;

namespace OopsType.Services;

public sealed class KeyboardLayoutService : IKeyboardLayoutService
{
    private readonly DispatcherTimer _poll;
    private WinEventHook? _focusHook;
    private LanguageInfo _current = LanguageInfo.Unknown;

    public LanguageInfo Current => _current;
    public event Action<LanguageInfo>? LanguageChanged;

    public KeyboardLayoutService()
    {
        _poll = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(80),
        };
        _poll.Tick += (_, _) => CheckLayout();
    }

    public void Start()
    {
        _focusHook = new WinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_OBJECT_FOCUS);
        _focusHook.EventRaised += (_, _) => CheckLayout();
        _poll.Start();
        CheckLayout();
    }

    private void CheckLayout()
    {
        var info = GetForegroundLayout();
        if (info.Hkl != _current.Hkl)
        {
            _current = info;
            LanguageChanged?.Invoke(info);
        }
    }

    public static LanguageInfo GetForegroundLayout()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return LanguageInfo.Unknown;
        var tid = NativeMethods.GetWindowThreadProcessId(hwnd, IntPtr.Zero);
        var hkl = NativeMethods.GetKeyboardLayout(tid);
        return BuildInfo(hkl);
    }

    private static LanguageInfo BuildInfo(IntPtr hkl)
    {
        var langId = NativeMethods.LangIdFromHkl(hkl);
        if (langId == 0) return LanguageInfo.Unknown;

        var iso = QueryLocale(langId, NativeMethods.LOCALE_SISO639LANGNAME);
        var full = QueryLocale(langId, NativeMethods.LOCALE_SENGLISHLANGUAGENAME);
        var code = string.IsNullOrWhiteSpace(iso) ? "??" : iso.ToUpperInvariant();
        var label = ComputeDisplayLabel(langId, code);
        return new LanguageInfo(hkl, langId, code, string.IsNullOrEmpty(full) ? code : full, label);
    }

    /// <summary>
    /// Returns the language abbreviation in the language's own script:
    ///   en → "EN", he → "עב", ru → "РУ", el → "ΕΛ", ja → "日本".
    /// Falls back to the ISO code for Latin-script languages so we don't truncate
    /// "English" into "EN" awkwardly.
    /// </summary>
    private static string ComputeDisplayLabel(int langId, string isoUpper)
    {
        string? native = null;
        try { native = System.Globalization.CultureInfo.GetCultureInfo(langId).NativeName; }
        catch { /* unknown LCID — fall through to ISO */ }

        if (string.IsNullOrWhiteSpace(native)) return isoUpper;

        var first = native[0];
        bool isBasicLatinLetter = (first >= 'a' && first <= 'z') || (first >= 'A' && first <= 'Z');
        if (isBasicLatinLetter) return isoUpper;

        var take = Math.Min(2, native.Length);
        return native.Substring(0, take).ToUpperInvariant();
    }

    private static string QueryLocale(int langId, uint lcType)
    {
        var sb = new StringBuilder(85);
        var n = NativeMethods.GetLocaleInfoW((uint)langId, lcType, sb, sb.Capacity);
        return n > 0 ? sb.ToString().TrimEnd('\0') : string.Empty;
    }

    public IReadOnlyList<LanguageInfo> GetInstalledLayouts()
    {
        var list = new List<LanguageInfo>();
        var count = NativeGetKeyboardLayoutList(0, null);
        if (count <= 0) return list;
        var arr = new IntPtr[count];
        NativeGetKeyboardLayoutList(count, arr);
        var seen = new HashSet<int>();
        foreach (var hkl in arr)
        {
            var info = BuildInfo(hkl);
            if (seen.Add(info.LangId)) list.Add(info);
        }
        return list;
    }

    public bool RequestLanguage(string twoLetterCode)
    {
        if (string.IsNullOrWhiteSpace(twoLetterCode)) return false;
        IntPtr target = IntPtr.Zero;
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
        // wParam = 0 → use HKL in lParam directly. Sending INPUTLANGCHANGE_FORWARD ignores lParam.
        NativeMethods.PostMessage(hwnd, NativeMethods.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target);
        return true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetKeyboardLayoutList")]
    private static extern int NativeGetKeyboardLayoutList(int nBuff, IntPtr[]? lpList);

    public void Dispose()
    {
        _poll.Stop();
        _focusHook?.Dispose();
    }
}
