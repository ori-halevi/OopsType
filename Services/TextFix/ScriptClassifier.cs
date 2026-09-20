using System;

namespace OopsType.Services.TextFix;

/// <summary>Writing systems we can tell apart well enough to pick a source keyboard layout.</summary>
public enum TextScript
{
    /// <summary>Whitespace, digits, punctuation, symbols — carries no language signal.</summary>
    Neutral,
    Latin,
    Hebrew,
    Arabic,
    Cyrillic,
    Greek,
    Armenian,
    Georgian,
    Thai,
    Devanagari,
    /// <summary>A letter from a script we don't classify. Never matched against a layout.</summary>
    Other,
}

/// <summary>
/// Pure Unicode-range classification, with no I/O and no layout awareness — the counterpart to
/// <see cref="LocaleResolver"/> for text rather than for HKLs.
///
/// <para>The whole point of classifying by SCRIPT rather than by language is extensibility: a
/// Russian or Greek user gets correct source detection without a single line of per-language
/// configuration, because <see cref="LayoutTransposer.GetLayoutScript"/> asks each installed layout
/// what it produces and classifies THAT through this same function.</para>
///
/// <para><b>Why "neutral" matters.</b> A sentence typed in the wrong layout is riddled with spaces,
/// commas and digits, which every layout shares. If those counted as script evidence, every real
/// sentence would look "mixed" and never convert. They're excluded, so the uniformity test in
/// <see cref="TrySingleScript"/> only ever looks at genuine letters.</para>
/// </summary>
public static class ScriptClassifier
{
    public static TextScript Classify(char c)
    {
        // Control characters and everything the framework considers non-letter carries no signal.
        // Checked before the range table so a digit inside a Cyrillic range can't be misread.
        if (char.IsWhiteSpace(c) || char.IsDigit(c) || char.IsPunctuation(c)
            || char.IsSymbol(c) || char.IsControl(c) || char.IsSeparator(c))
            return TextScript.Neutral;

        return c switch
        {
            // Basic Latin letters, Latin-1 Supplement letters, Latin Extended-A/B, IPA.
            >= '\u0041' and <= '\u005A' => TextScript.Latin,
            >= '\u0061' and <= '\u007A' => TextScript.Latin,
            >= '\u00C0' and <= '\u024F' => TextScript.Latin,
            >= '\u1E00' and <= '\u1EFF' => TextScript.Latin,   // Latin Extended Additional

            >= '\u0370' and <= '\u03FF' => TextScript.Greek,
            >= '\u1F00' and <= '\u1FFF' => TextScript.Greek,   // Greek Extended

            >= '\u0400' and <= '\u052F' => TextScript.Cyrillic,

            >= '\u0530' and <= '\u058F' => TextScript.Armenian,

            // Hebrew block, plus the Alphabetic Presentation Forms tail (ligatures, pointed forms).
            >= '\u0590' and <= '\u05FF' => TextScript.Hebrew,
            >= '\uFB1D' and <= '\uFB4F' => TextScript.Hebrew,

            // Arabic and its supplements/presentation forms.
            >= '\u0600' and <= '\u06FF' => TextScript.Arabic,
            >= '\u0750' and <= '\u077F' => TextScript.Arabic,
            >= '\uFB50' and <= '\uFDFF' => TextScript.Arabic,
            >= '\uFE70' and <= '\uFEFF' => TextScript.Arabic,

            >= '\u0900' and <= '\u097F' => TextScript.Devanagari,
            >= '\u0E00' and <= '\u0E7F' => TextScript.Thai,
            >= '\u10A0' and <= '\u10FF' => TextScript.Georgian,

            _ => TextScript.Other,
        };
    }

    /// <summary>
    /// True when every non-neutral character in <paramref name="text"/> belongs to one and the same
    /// script, which it reports in <paramref name="script"/>.
    ///
    /// <para>False means either "no letters at all" (nothing to convert) or "more than one script"
    /// — a deliberately conservative refusal. Mixed-script text is exactly the case where a wrong
    /// guess is hardest for the user to walk back, so we decline instead of half-converting it.
    /// <see cref="TextScript.Other"/> counts as a script for this test, so an unclassified alphabet
    /// still trips the mixed check rather than being silently ignored.</para>
    /// </summary>
    public static bool TrySingleScript(string text, out TextScript script)
    {
        script = TextScript.Neutral;
        if (string.IsNullOrEmpty(text)) return false;

        foreach (var c in text)
        {
            var s = Classify(c);
            if (s == TextScript.Neutral) continue;
            if (script == TextScript.Neutral) { script = s; continue; }
            if (s != script)
            {
                script = TextScript.Neutral;
                return false;
            }
        }

        return script != TextScript.Neutral;
    }
}
