using System;
using System.Collections.Generic;
using OopsType.Models;

namespace OopsType.Services.TextFix;

/// <param name="Resolved">False when no safe (source, target) pair could be established.</param>
/// <param name="Source">Layout the text was actually typed on.</param>
/// <param name="Target">Layout the user meant to type on.</param>
public readonly record struct ConversionDirection(bool Resolved, LanguageInfo Source, LanguageInfo Target)
{
    public static readonly ConversionDirection None = new(false, LanguageInfo.Unknown, LanguageInfo.Unknown);
}

/// <summary>
/// Decides which way to convert.
///
/// <para><b>The target is always the layout just switched TO.</b> Switching language states an
/// intent — "this is what I want to be writing in" — and it means the same thing whether or not
/// something happens to be selected. So the switch fixes the destination, and the only remaining
/// question is who wrote the selected text, which the script it is written in answers.</para>
///
/// <para>What this rule prevents matters more than what it allows. Selecting a perfectly good Hebrew
/// word and switching to Hebrew is simply how people retype a word; treating the switch as "convert
/// between these two layouts" would turn that word into Latin noise. Anchoring on the target makes
/// the selection already correct, so nothing happens.</para>
///
/// <para>Undo is unaffected: putting converted text back means switching to the language you want it
/// in, which is exactly what this rule describes.</para>
///
/// <para><b>Known limit.</b> Two layouts sharing one script (en/fr, en/de) can never be told apart
/// this way — the text looks the same either way — so a switch between them converts nothing.</para>
/// </summary>
internal static class DirectionResolver
{
    /// <summary>
    /// Fixes the target from the switch itself, then identifies the source from the selection.
    /// </summary>
    /// <param name="selectionScript">Single script the selection was found to be written in.</param>
    /// <param name="previous">Layout active immediately before the switch.</param>
    /// <param name="current">Layout active now — always the target.</param>
    /// <param name="installed">All installed layouts, searched when the previous one did not write this text.</param>
    internal static ConversionDirection Resolve(
        TextScript selectionScript,
        LanguageInfo previous,
        LanguageInfo current,
        IReadOnlyList<LanguageInfo> installed)
    {
        if (selectionScript == TextScript.Neutral || selectionScript == TextScript.Other)
            return ConversionDirection.None;
        if (previous.Hkl == IntPtr.Zero || current.Hkl == IntPtr.Zero) return ConversionDirection.None;
        if (previous.Hkl == current.Hkl) return ConversionDirection.None;

        // The text is already written in the language being switched to, so there is nothing to
        // bring it to. This is the whole point of anchoring on the target: selecting a correct
        // Hebrew word and switching to Hebrew is how people retype a word, and it must not be
        // mistaken for a request to turn that word into Latin noise.
        var currentScript = LayoutTransposer.GetLayoutScript(current.Hkl);
        if (currentScript == selectionScript) return ConversionDirection.None;

        // Whoever wrote the selection is the source. Normally that is the layout just left behind.
        var source = LayoutTransposer.GetLayoutScript(previous.Hkl) == selectionScript
            ? previous
            // Otherwise the user had already moved off it before selecting; salvage the conversion
            // only if exactly one installed layout writes this script, since anything ambiguous
            // would be a guess about text we are about to overwrite.
            : FindUniqueByScript(selectionScript, installed);

        if (source == null || source.Hkl == current.Hkl) return ConversionDirection.None;

        return new ConversionDirection(true, source, current);
    }

    private static LanguageInfo? FindUniqueByScript(TextScript script, IReadOnlyList<LanguageInfo> installed)
    {
        LanguageInfo? match = null;
        foreach (var layout in installed)
        {
            if (LayoutTransposer.GetLayoutScript(layout.Hkl) != script) continue;
            if (match != null) return null;   // ambiguous — two layouts write this script
            match = layout;
        }
        return match;
    }
}
