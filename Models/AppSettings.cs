using System.Collections.Generic;

namespace OopsType.Models;

public sealed class AppSettings
{
    public CaretLabelSettings CaretLabel { get; set; } = new();
    public MouseLabelSettings MouseLabel { get; set; } = new();
    public TaskbarStripSettings TaskbarStrip { get; set; } = new();
    public IdleResetSettings IdleReset { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
}

public sealed class CaretLabelSettings
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// How the chip's HORIZONTAL position is decided:
    ///   "offset" (default) — a fixed signed <see cref="OffsetX"/> from the caret (legacy behavior).
    ///   "auto" — pick the side from the active keyboard language so the chip never covers the text:
    ///            RTL language → left of the caret, LTR language → right of it, separated by
    ///            <see cref="HorizontalDistance"/>. Lets the user switch languages without the chip
    ///            ever sitting on top of what they're typing.
    /// </summary>
    public string HorizontalMode { get; set; } = "offset";

    /// <summary>Gap in DIPs between the caret and the chip when <see cref="HorizontalMode"/> is
    /// "auto". Non-negative; the side (left/right) is chosen from the language direction.</summary>
    public int HorizontalDistance { get; set; } = 8;

    /// <summary>Horizontal offset of the chip's CENTRE from the caret, in DIPs. 0 centres the chip on
    /// the caret; positive moves it right, negative left. Used when <see cref="HorizontalMode"/> is "offset".</summary>
    public int OffsetX { get; set; } = 0;
    /// <summary>Vertical offset of the chip's CENTRE from the caret, in DIPs. 0 centres the chip on the
    /// caret line; positive moves it UP, negative DOWN.</summary>
    public int OffsetY { get; set; } = 0;
    public string Font { get; set; } = "Segoe UI";
    public int Size { get; set; } = 11;

    /// <summary>Label text weight — one of Light/Regular/Medium/SemiBold/Bold (case-insensitive).
    /// Default Bold. Unrecognised values fall back to Bold (see LabelStyleBrushes.ParseFontWeight).</summary>
    public string FontWeight { get; set; } = "Bold";

    /// <summary>0.0 = invisible, 1.0 = fully opaque. Applied to the whole chip (background, text, border).</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>0.0 = invisible text (only the empty chip remains), 1.0 = fully opaque. Applied to the
    /// label TEXT only, multiplying on top of the chip-wide <see cref="Opacity"/>.</summary>
    public double TextOpacity { get; set; } = 1.0;

    /// <summary>Per-language chip appearance. Key is the two-letter layout code (lowercase, e.g. "he").
    /// A language with no entry falls back to the built-in dark chip with white text and no border.</summary>
    public Dictionary<string, LabelLangStyle> Colors { get; set; } = new();
}

public sealed class MouseLabelSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Offset from the cursor hotspot, in DIPs. Horizontally the chip is CENTRED on the tip
    /// (X positive-right); vertically its TOP edge sits at the tip when OffsetY is 0, so the chip
    /// hangs below the cursor like a tooltip (Y positive-up). The default 14,0 leaves the top at the
    /// tip and nudges the chip just right of centre.</summary>
    public int OffsetX { get; set; } = 14;
    public int OffsetY { get; set; } = 0;
    public string Font { get; set; } = "Segoe UI";
    public int Size { get; set; } = 11;

    /// <summary>Label text weight — one of Light/Regular/Medium/SemiBold/Bold (case-insensitive).
    /// Default Bold. Unrecognised values fall back to Bold (see LabelStyleBrushes.ParseFontWeight).</summary>
    public string FontWeight { get; set; } = "Bold";

    /// <summary>0.0 = invisible, 1.0 = fully opaque. Applied to the whole chip (background, text, border).</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>0.0 = invisible text (only the empty chip remains), 1.0 = fully opaque. Applied to the
    /// label TEXT only, multiplying on top of the chip-wide <see cref="Opacity"/>.</summary>
    public double TextOpacity { get; set; } = 1.0;

    /// <summary>Per-language chip appearance. Key is the two-letter layout code (lowercase, e.g. "he").
    /// A language with no entry falls back to the built-in dark chip with white text and no border.</summary>
    public Dictionary<string, LabelLangStyle> Colors { get; set; } = new();

    /// <summary>
    /// "economy" (default) — subscribe to CompositionTarget.Rendering only while the mouse is
    /// moving (driven by a global mouse hook), unsubscribe shortly after it stops. Zero work at
    /// idle, fits 24/7 use and laptops.
    /// "max-smoothness" — keep the Rendering subscription alive permanently. Marginally snappier
    /// on the first frame of motion, at the cost of a per-frame tick that never stops.
    /// </summary>
    public string TrackingMode { get; set; } = "economy";
}

/// <summary>
/// Per-language appearance for a caret/mouse label chip. Hex strings accept #RGB, #RRGGBB and
/// #AARRGGBB (so the user can encode per-color alpha directly). <see cref="BorderThickness"/> in
/// DIPs — 0 means "no border", in which case <see cref="BorderColor"/> is ignored.
/// </summary>
public sealed class LabelLangStyle
{
    /// <summary>Chip background (fill) color.</summary>
    public string Background { get; set; } = "#CC222222";
    /// <summary>Text (glyph) color.</summary>
    public string Foreground { get; set; } = "#FFFFFFFF";
    /// <summary>Border stroke color. Ignored when <see cref="BorderThickness"/> is 0.</summary>
    public string BorderColor { get; set; } = "#FF000000";
    /// <summary>Border stroke width in DIPs. 0 = no border.</summary>
    public double BorderThickness { get; set; } = 0;
}

public sealed class TaskbarStripSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>"small" (3px) | "medium" (8px) | "large" (16px) | "full" (entire taskbar height).</summary>
    public string Thickness { get; set; } = "small";

    /// <summary>"top" | "bottom" — anchor point within the taskbar's Y span. Ignored when Thickness=full.</summary>
    public string VerticalPosition { get; set; } = "top";

    /// <summary>When false, Opacity is ignored and the strip renders fully opaque.</summary>
    public bool OpacityEnabled { get; set; } = true;

    /// <summary>0.0 = invisible, 1.0 = fully opaque. Default 0.6 so the taskbar shows through.</summary>
    public double Opacity { get; set; } = 0.6;

    /// <summary>"front" = on top of the taskbar | "behind" = below it (visible through Win11 acrylic).</summary>
    public string Placement { get; set; } = "front";

    public Dictionary<string, string> Colors { get; set; } = new()
    {
        ["he"] = "#2E7D32",
        ["en"] = "#1565C0",
    };
}

public sealed class IdleResetSettings
{
    public bool Enabled { get; set; } = false;
    public int IdleSeconds { get; set; } = 120;
    public string TargetLang { get; set; } = "en";
}

public sealed class GeneralSettings
{
    public bool Autostart { get; set; } = true;

    /// <summary>
    /// Code of the language pack used by the settings window and tray menu (e.g. "en", "he").
    /// Empty means "use default" — LocalizationService resolves it against whatever packs it
    /// discovered, falling back to English if none match.
    /// </summary>
    public string Language { get; set; } = "";
}
