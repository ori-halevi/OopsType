using System;

namespace OopsType.Models;

/// <param name="TwoLetterCode">ISO 639 code (e.g. "EN", "HE"). Stable identifier used for color/idle lookups.</param>
/// <param name="DisplayLabel">Short label in the language's own script (e.g. "EN", "עב", "РУ"). For UI.</param>
/// <param name="IsRtl">True when the language's script flows right-to-left (Hebrew, Arabic, …). Used
/// by the caret label's "auto horizontal position" mode to place the chip on the side away from text.</param>
public sealed record LanguageInfo(
    IntPtr Hkl, int LangId, string TwoLetterCode, string FullName, string DisplayLabel, bool IsRtl = false)
{
    public static readonly LanguageInfo Unknown = new(IntPtr.Zero, 0, "??", "Unknown", "??", false);
}
