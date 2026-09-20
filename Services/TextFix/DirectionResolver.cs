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
/// <para>The direction of the user's language switch is deliberately NOT the deciding signal — the
/// text itself is. Someone who notices the mistake often switches back to the right language first
/// and only then selects the mess, so trusting the switch direction would convert backwards exactly
/// when the user is being careful. The script the selection is written in says unambiguously which
/// layout produced it; the target is then simply the other one.</para>
/// </summary>
internal static class DirectionResolver
{
    /// <summary>
    /// Pairs the selection's script with a source layout and picks the target.
    /// </summary>
    /// <param name="selectionScript">Single script the selection was found to be written in.</param>
    /// <param name="previous">Layout active immediately before the switch.</param>
    /// <param name="current">Layout active now.</param>
    /// <param name="installed">All installed layouts, used only when neither side matches.</param>
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

        var previousScript = LayoutTransposer.GetLayoutScript(previous.Hkl);
        var currentScript = LayoutTransposer.GetLayoutScript(current.Hkl);

        // Ordinary case: the user typed on the layout they just left, so that is the source.
        // Checked first so an en-to-fr switch (both Latin) resolves in the switch's own direction.
        if (previousScript == selectionScript)
            return new ConversionDirection(true, previous, current);

        // The user had already corrected the layout before selecting: the text is in the script of
        // the layout they just switched TO, so convert it the other way.
        if (currentScript == selectionScript)
            return new ConversionDirection(true, current, previous);

        // Neither side wrote this text — the user switched between two layouts unrelated to the
        // selection. Salvage it only if exactly one installed layout writes in that script;
        // anything ambiguous is left alone.
        var source = FindUniqueByScript(selectionScript, installed);
        if (source == null) return ConversionDirection.None;

        var target = source.Hkl != current.Hkl ? current : previous;
        if (target.Hkl == source.Hkl) return ConversionDirection.None;

        return new ConversionDirection(true, source, target);
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
