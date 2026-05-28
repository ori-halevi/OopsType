using System;

namespace OopsType.Models;

/// <param name="TwoLetterCode">ISO 639 code (e.g. "EN", "HE"). Stable identifier used for color/idle lookups.</param>
/// <param name="DisplayLabel">Short label in the language's own script (e.g. "EN", "עב", "РУ"). For UI.</param>
public sealed record LanguageInfo(
    IntPtr Hkl, int LangId, string TwoLetterCode, string FullName, string DisplayLabel)
{
    public static readonly LanguageInfo Unknown = new(IntPtr.Zero, 0, "??", "Unknown", "??");
}
