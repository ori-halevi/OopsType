using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace OopsType.Views.Controls;

/// <summary>
/// A full color field: shows the current color as a swatch button; clicking it opens a popup with a
/// saturation/brightness area, a hue slider, an alpha slider, hex + R/G/B entry and quick presets.
/// The chosen color is committed to <see cref="SelectedColor"/> (a normalized "#AARRGGBB" string)
/// only on <em>Apply</em>, so the bound row/table preview updates exactly when the user confirms —
/// Cancel discards the in-popup edits. Internally the control works in HSVA so the SV area and the
/// hue/alpha sliders move independently the way users expect.
/// </summary>
public partial class ColorPickerButton : UserControl
{
    // Working state while the popup is open. Kept in HSVA (not RGB) so dragging the SV area doesn't
    // perturb the hue, and grays keep their last hue instead of snapping to red.
    private double _h;   // hue        0..360
    private double _s;   // saturation 0..1
    private double _v;   // value      0..1
    private double _a;   // alpha      0..1

    // Guards the text boxes against re-entrancy: RefreshUi writes the hex/RGB boxes, whose
    // TextChanged handlers would otherwise feed back into the state and fight the user's input.
    private bool _suppress;

    public ColorPickerButton()
    {
        InitializeComponent();
        Presets = BuildPresets();
        // Seed the swatch brush from the initial color so it shows immediately, before any change.
        SelectedBrush = BrushFor(SelectedColor);
    }

    /// <summary>The committed color as a hex string (e.g. "#FF2E7D32"). Two-way bindable.</summary>
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(string), typeof(ColorPickerButton),
        new FrameworkPropertyMetadata("#FF888888", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    public string SelectedColor
    {
        get => (string)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>
    /// Read-only <see cref="Brush"/> projection of <see cref="SelectedColor"/>, kept in sync in
    /// code. The swatch binds to THIS (Brush→Brush) rather than binding the hex string directly:
    /// WPF's implicit string→Brush conversion is unreliable for ElementName bindings and a shared
    /// converter resource is not reliably reachable from inside this UserControl when it's realized
    /// within a DataTemplate — both routes left the swatch blank. A computed brush can't fail.
    /// </summary>
    public static readonly DependencyProperty SelectedBrushProperty = DependencyProperty.Register(
        nameof(SelectedBrush), typeof(Brush), typeof(ColorPickerButton),
        new PropertyMetadata(Brushes.Transparent));

    public Brush SelectedBrush
    {
        get => (Brush)GetValue(SelectedBrushProperty);
        private set => SetValue(SelectedBrushProperty, value);
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ColorPickerButton)d).SelectedBrush = BrushFor(e.NewValue as string);

    private static Brush BrushFor(string? hex)
    {
        if (TryParseColor(hex, out var c))
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        return Brushes.Transparent;
    }

    /// <summary>Quick-pick swatches shown under the sliders. Read-only after construction.</summary>
    public IReadOnlyList<string> Presets { get; }

    // ---- open / commit ------------------------------------------------------------------------
    private void OnOpen(object sender, RoutedEventArgs e)
    {
        SetFromColor(ParseOr(SelectedColor, WpfColor.FromArgb(0xFF, 0x88, 0x88, 0x88)));
        Popup.IsOpen = true;
        // The thumbs and gradients are positioned from the bars' ActualWidth/Height, which are only
        // valid after the popup has laid out — push the visual state once layout has happened.
        Dispatcher.BeginInvoke(new Action(() => RefreshUi()),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        // The state is always a valid color, so committing it never produces garbage even if the
        // hex box currently holds a half-typed value.
        SelectedColor = ToHex(CurrentColor());
        Popup.IsOpen = false;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Popup.IsOpen = false;

    // ---- drag bars (SV area, hue, alpha) ------------------------------------------------------
    private void OnBarDown(object sender, MouseButtonEventArgs e)
    {
        var el = (UIElement)sender;
        el.CaptureMouse();
        UpdateFromBar(sender, e.GetPosition((IInputElement)sender));
    }

    private void OnBarMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && ((UIElement)sender).IsMouseCaptured)
            UpdateFromBar(sender, e.GetPosition((IInputElement)sender));
    }

    private void OnBarUp(object sender, MouseButtonEventArgs e) => ((UIElement)sender).ReleaseMouseCapture();

    private void UpdateFromBar(object sender, Point p)
    {
        if (ReferenceEquals(sender, SvBox))
        {
            _s = Clamp01(p.X / Math.Max(1.0, SvBox.ActualWidth));
            _v = Clamp01(1 - p.Y / Math.Max(1.0, SvBox.ActualHeight));
        }
        else if (ReferenceEquals(sender, HueBar))
        {
            _h = Clamp01(p.X / Math.Max(1.0, HueBar.ActualWidth)) * 360.0;
        }
        else if (ReferenceEquals(sender, AlphaBar))
        {
            _a = Clamp01(p.X / Math.Max(1.0, AlphaBar.ActualWidth));
        }
        RefreshUi();
    }

    // ---- text / preset inputs -----------------------------------------------------------------
    private void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        if (TryParseColor(HexBox.Text, out var c))
        {
            SetFromColor(c);
            RefreshUi(skipHex: true); // leave the box the user is typing in alone
        }
    }

    private void OnRgbChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        if (byte.TryParse(RBox.Text, out var r) && byte.TryParse(GBox.Text, out var g) && byte.TryParse(BBox.Text, out var b))
        {
            SetFromColor(WpfColor.FromArgb((byte)Math.Round(_a * 255), r, g, b));
            RefreshUi(skipRgb: true);
        }
    }

    private void OnPresetPick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string hex && TryParseColor(hex, out var c))
        {
            // Presets only change the hue/shade; keep whatever alpha the user already dialed in.
            SetFromColor(WpfColor.FromArgb((byte)Math.Round(_a * 255), c.R, c.G, c.B));
            RefreshUi();
        }
    }

    // ---- state <-> ui ------------------------------------------------------------------------
    private WpfColor CurrentColor() => HsvToColor(_h, _s, _v, _a);

    private void SetFromColor(WpfColor c)
    {
        _a = c.A / 255.0;
        var (h, s, v) = RgbToHsv(c);
        // For achromatic colors (s or v == 0) hue is undefined; keep the last hue so the SV thumb
        // and hue slider don't jump to red when the user picks black/white/gray.
        if (s > 0 && v > 0) _h = h;
        _s = s;
        _v = v;
    }

    private void RefreshUi(bool skipHex = false, bool skipRgb = false)
    {
        _suppress = true;
        try
        {
            var col = CurrentColor();

            // SV area base hue + thumb.
            SvHue.Background = new SolidColorBrush(HsvToColor(_h, 1, 1, 1));
            PlaceThumb(SvThumb, _s * SvBox.ActualWidth, (1 - _v) * SvBox.ActualHeight);

            // Hue slider thumb (X only).
            PlaceThumb(HueThumb, _h / 360.0 * HueBar.ActualWidth, HueBar.ActualHeight / 2);

            // Alpha slider gradient (transparent -> opaque current color) + thumb.
            var opaque = HsvToColor(_h, _s, _v, 1);
            AlphaStop0.Color = WpfColor.FromArgb(0, opaque.R, opaque.G, opaque.B);
            AlphaStop1.Color = opaque;
            PlaceThumb(AlphaThumb, _a * AlphaBar.ActualWidth, AlphaBar.ActualHeight / 2);

            PreviewSwatch.Background = new SolidColorBrush(col);

            if (!skipHex) HexBox.Text = ToHex(col);
            if (!skipRgb)
            {
                RBox.Text = col.R.ToString(CultureInfo.InvariantCulture);
                GBox.Text = col.G.ToString(CultureInfo.InvariantCulture);
                BBox.Text = col.B.ToString(CultureInfo.InvariantCulture);
            }
        }
        finally { _suppress = false; }
    }

    private static void PlaceThumb(FrameworkElement thumb, double centerX, double centerY)
    {
        Canvas.SetLeft(thumb, centerX - thumb.Width / 2);
        Canvas.SetTop(thumb, centerY - thumb.Height / 2);
    }

    // ---- color helpers ------------------------------------------------------------------------
    private static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

    private static string ToHex(WpfColor c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static WpfColor ParseOr(string? hex, WpfColor fallback)
        => TryParseColor(hex, out var c) ? c : fallback;

    private static bool TryParseColor(string? hex, out WpfColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try { color = (WpfColor)ColorConverter.ConvertFromString(hex.Trim()); return true; }
        catch { return false; }
    }

    private static WpfColor HsvToColor(double h, double s, double v, double a)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;
        switch ((int)(h / 60) % 6)
        {
            case 0: r = c; g = x; break;
            case 1: r = x; g = c; break;
            case 2: g = c; b = x; break;
            case 3: g = x; b = c; break;
            case 4: r = x; b = c; break;
            default: r = c; b = x; break;
        }
        return WpfColor.FromArgb(
            (byte)Math.Round(Clamp01(a) * 255),
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static (double h, double s, double v) RgbToHsv(WpfColor c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * ((g - b) / d % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }
        double s = max <= 0 ? 0 : d / max;
        return (h, s, max);
    }

    // A compact, curated preset row (rainbow + neutrals). Clicking one keeps the current alpha.
    private static IReadOnlyList<string> BuildPresets() => new[]
    {
        "#FFEF4444", "#FFF97316", "#FFF59E0B", "#FFEAB308", "#FF22C55E",
        "#FF14B8A6", "#FF06B6D4", "#FF3B82F6", "#FF6366F1", "#FF8B5CF6",
        "#FFEC4899", "#FFFFFFFF", "#FF9CA3AF", "#FF111827",
    };
}
