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
    /// <summary>Horizontal offset from caret's top-left, in DIPs.</summary>
    public int OffsetX { get; set; } = 0;
    /// <summary>Vertical offset from caret-top minus label-height (so 0 = just above the caret line).</summary>
    public int OffsetY { get; set; } = 0;
    public string Font { get; set; } = "Segoe UI";
    public int Size { get; set; } = 11;

    /// <summary>0.0 = invisible, 1.0 = fully opaque. Applied to the whole chip (background, text, border).</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Per-language chip appearance. Key is the two-letter layout code (lowercase, e.g. "he").
    /// A language with no entry falls back to the built-in dark chip with white text and no border.</summary>
    public Dictionary<string, LabelLangStyle> Colors { get; set; } = new();
}

public sealed class MouseLabelSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Offset from the cursor hotspot, in DIPs. Default 12,12 = bottom-right of cursor.</summary>
    public int OffsetX { get; set; } = 12;
    public int OffsetY { get; set; } = 12;
    public string Font { get; set; } = "Segoe UI";
    public int Size { get; set; } = 11;

    /// <summary>0.0 = invisible, 1.0 = fully opaque. Applied to the whole chip (background, text, border).</summary>
    public double Opacity { get; set; } = 1.0;

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
