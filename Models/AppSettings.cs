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
}

public sealed class MouseLabelSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Offset from the cursor hotspot, in DIPs. Default 12,12 = bottom-right of cursor.</summary>
    public int OffsetX { get; set; } = 12;
    public int OffsetY { get; set; } = 12;
    public string Font { get; set; } = "Segoe UI";
    public int Size { get; set; } = 11;

    /// <summary>
    /// "economy" (default) — subscribe to CompositionTarget.Rendering only while the mouse is
    /// moving (driven by a global mouse hook), unsubscribe shortly after it stops. Zero work at
    /// idle, fits 24/7 use and laptops.
    /// "max-smoothness" — keep the Rendering subscription alive permanently. Marginally snappier
    /// on the first frame of motion, at the cost of a per-frame tick that never stops.
    /// </summary>
    public string TrackingMode { get; set; } = "economy";
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
}
