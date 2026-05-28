using System;
using System.Globalization;
using System.Text;
using OopsType.Models;
using OopsType.Native;

namespace OopsType.Services;

/// <summary>
/// Translates a raw <c>HKL</c> (keyboard layout handle) into a fully-populated <see cref="LanguageInfo"/>.
/// Extracted from <c>KeyboardLayoutService</c> so the polling/eventing concern stays separate from
/// the locale-resolution concern (single responsibility per class).
/// </summary>
internal static class LocaleResolver
{
    public static LanguageInfo Resolve(IntPtr hkl)
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
    /// For Latin-script languages we keep the ISO code so we don't truncate "English" awkwardly.
    /// </summary>
    private static string ComputeDisplayLabel(int langId, string isoUpper)
    {
        string? native = null;
        try { native = CultureInfo.GetCultureInfo(langId).NativeName; }
        catch { /* unknown LCID — fall through to ISO code */ }

        if (string.IsNullOrWhiteSpace(native)) return isoUpper;

        var first = native[0];
        var isBasicLatinLetter = (first >= 'a' && first <= 'z') || (first >= 'A' && first <= 'Z');
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
}
