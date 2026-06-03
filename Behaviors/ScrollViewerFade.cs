using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OopsType.Behaviors;

/// <summary>
/// Attaches a soft top/bottom fade to a <see cref="ScrollViewer"/> as a scroll affordance.
///
/// The Fluent overlay scrollbar is thin and auto-hides, so when a settings page has more cards
/// below the fold nothing signals that it scrolls — users assume the visible content is all there
/// is. This draws a gradient scrim at the bottom edge while there is more content below (and at the
/// top edge once scrolled down), which reads as "content continues past this edge".
///
/// The scrim is painted by a non-hit-testable <see cref="Adorner"/> over the scroll viewport, so it
/// overlays the page without touching its layout. That means it can be switched on for every page
/// at once via a single Style setter on the shared ScrollViewer style — no per-page markup.
/// </summary>
public static class ScrollViewerFade
{
    // Height of the gradient strip at each edge, in DIPs.
    private const double FadeHeight = 40;

    // Maximum times we retry locating the adorner layer before giving up (the layer only exists
    // once the ScrollViewer is parented under the window's AdornerDecorator). Degrading to "no
    // fade" is harmless, so we never want to spin forever.
    private const int MaxAttachAttempts = 5;

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(ScrollViewerFade),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

    // Holds the live adorner so we can update / remove it later without a side table.
    private static readonly DependencyProperty AdornerProperty =
        DependencyProperty.RegisterAttached(
            "Adorner", typeof(ScrollFadeAdorner), typeof(ScrollViewerFade), new PropertyMetadata(null));

    private static ScrollFadeAdorner? GetAdorner(DependencyObject d) => (ScrollFadeAdorner?)d.GetValue(AdornerProperty);
    private static void SetAdorner(DependencyObject d, ScrollFadeAdorner? value) => d.SetValue(AdornerProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;

        if ((bool)e.NewValue)
        {
            // The adorner layer only exists once the element is in the visual tree, so defer wiring
            // to Loaded. Loaded fires even for initially-collapsed pages, so every page gets its
            // adorner up front rather than only the one shown first.
            sv.Loaded += OnLoaded;
            if (sv.IsLoaded) TryAttach(sv, 0);
        }
        else
        {
            sv.Loaded -= OnLoaded;
            Detach(sv);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => TryAttach((ScrollViewer)sender, 0);

    private static void TryAttach(ScrollViewer sv, int attempt)
    {
        if (GetAdorner(sv) is not null) { Update(sv); return; } // already wired

        var layer = AdornerLayer.GetAdornerLayer(sv);
        if (layer is null)
        {
            if (attempt >= MaxAttachAttempts) return;
            sv.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => TryAttach(sv, attempt + 1)));
            return;
        }

        var adorner = new ScrollFadeAdorner(sv, FadeHeight);
        layer.Add(adorner);
        SetAdorner(sv, adorner);

        sv.ScrollChanged += OnScrollChanged;
        sv.SizeChanged += OnSizeChanged;
        sv.IsVisibleChanged += OnIsVisibleChanged;

        Update(sv);
        // ScrollableHeight is still 0 until the content is measured, so recompute once layout settles.
        sv.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Update(sv)));
    }

    private static void Detach(ScrollViewer sv)
    {
        sv.ScrollChanged -= OnScrollChanged;
        sv.SizeChanged -= OnSizeChanged;
        sv.IsVisibleChanged -= OnIsVisibleChanged;

        if (GetAdorner(sv) is { } adorner)
        {
            AdornerLayer.GetAdornerLayer(sv)?.Remove(adorner);
            SetAdorner(sv, null);
        }
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e) => Update((ScrollViewer)sender);

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Update((ScrollViewer)sender);

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        Update(sv);
        // A page that was collapsed isn't measured, so on the switch to visible its ScrollableHeight
        // is briefly stale; recompute after the pending layout pass.
        if (sv.IsVisible)
            sv.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Update(sv)));
    }

    private static void Update(ScrollViewer sv)
    {
        if (GetAdorner(sv) is not { } adorner) return;

        var visible = sv.IsVisible;
        var canScrollDown = visible && sv.ScrollableHeight > 0.5 && sv.VerticalOffset < sv.ScrollableHeight - 0.5;
        var canScrollUp = visible && sv.VerticalOffset > 0.5;

        adorner.SetEdges(top: canScrollUp, bottom: canScrollDown);
        adorner.InvalidateVisual(); // redraw at the current width/height after a resize
    }

    /// <summary>Paints the edge scrims. Opacities are animated by the host behavior so edges fade.</summary>
    private sealed class ScrollFadeAdorner : Adorner
    {
        private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(160));

        private readonly double _fadeHeight;
        private readonly Brush _bottomBrush;
        private readonly Brush _topBrush;

        public static readonly DependencyProperty TopOpacityProperty =
            DependencyProperty.Register(
                nameof(TopOpacity), typeof(double), typeof(ScrollFadeAdorner),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty BottomOpacityProperty =
            DependencyProperty.Register(
                nameof(BottomOpacity), typeof(double), typeof(ScrollFadeAdorner),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double TopOpacity { get => (double)GetValue(TopOpacityProperty); set => SetValue(TopOpacityProperty, value); }
        public double BottomOpacity { get => (double)GetValue(BottomOpacityProperty); set => SetValue(BottomOpacityProperty, value); }

        public ScrollFadeAdorner(UIElement adornedElement, double fadeHeight) : base(adornedElement)
        {
            _fadeHeight = fadeHeight;
            IsHitTestVisible = false; // never swallow clicks meant for the controls underneath

            // A faint shadow-like scrim. Painted over the window's Mica backdrop it reads as the page
            // content sliding under a soft edge, rather than a hard "fade to white" that would clash
            // with the translucent surface.
            var scrim = Color.FromArgb(0x29, 0x00, 0x00, 0x00); // ~16% black
            var clear = Color.FromArgb(0x00, 0x00, 0x00, 0x00);

            _bottomBrush = new LinearGradientBrush(clear, scrim, new Point(0, 0), new Point(0, 1));
            _bottomBrush.Freeze();
            _topBrush = new LinearGradientBrush(scrim, clear, new Point(0, 0), new Point(0, 1));
            _topBrush.Freeze();
        }

        public void SetEdges(bool top, bool bottom)
        {
            Animate(TopOpacityProperty, top ? 1.0 : 0.0);
            Animate(BottomOpacityProperty, bottom ? 1.0 : 0.0);
        }

        private void Animate(DependencyProperty property, double to)
        {
            if (Math.Abs((double)GetValue(property) - to) < 0.001) return;
            BeginAnimation(property, new DoubleAnimation(to, FadeDuration));
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            // The adorner has no measured size of its own, so size the scrims off the adorned viewport.
            var w = AdornedElement.RenderSize.Width;
            var h = AdornedElement.RenderSize.Height;
            if (w <= 0 || h <= 0) return;

            var strip = Math.Min(_fadeHeight, h / 2);

            if (BottomOpacity > 0.001)
            {
                drawingContext.PushOpacity(BottomOpacity);
                drawingContext.DrawRectangle(_bottomBrush, null, new Rect(0, h - strip, w, strip));
                drawingContext.Pop();
            }

            if (TopOpacity > 0.001)
            {
                drawingContext.PushOpacity(TopOpacity);
                drawingContext.DrawRectangle(_topBrush, null, new Rect(0, 0, w, strip));
                drawingContext.Pop();
            }
        }
    }
}
