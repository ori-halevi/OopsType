using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace OopsType.Infrastructure;

/// <summary>
/// Default <see cref="IToastService"/>. Owns a stack of small topmost windows in the bottom-right
/// of the primary work area; each one auto-fades after <see cref="VisibleDuration"/>. Multiple
/// concurrent toasts stack vertically and reposition as older ones expire.
/// </summary>
public sealed class ToastService : IToastService
{
    private static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(280);
    private const double EdgeMargin = 12;
    private const double GapBetween = 6;

    private readonly List<Window> _active = new();

    public void Show(string title, string message, ToastKind kind)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => Show(title, message, kind)));
            return;
        }

        try
        {
            var w = BuildToastWindow(title, message, kind);
            _active.Add(w);

            // Reposition once we know our measured size — Loaded fires after layout.
            w.Loaded += (_, _) => Reposition();
            w.Closed += (_, _) =>
            {
                _active.Remove(w);
                Reposition();
            };

            w.Show();
            ScheduleFadeOut(w);
        }
        catch
        {
            // Toast is best-effort. If creation fails (theme provider failure, etc.) the log
            // entry that triggered us is already on disk — we don't escalate further.
        }
    }

    private void Reposition()
    {
        var wa = SystemParameters.WorkArea;
        // Stack bottom-up: newest toast sits at the bottom, older ones rise above it.
        var bottom = wa.Bottom - EdgeMargin;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var w = _active[i];
            if (w.ActualHeight <= 0 || w.ActualWidth <= 0) continue;
            w.Top = bottom - w.ActualHeight;
            w.Left = wa.Right - w.ActualWidth - EdgeMargin;
            bottom = w.Top - GapBetween;
        }
    }

    private static void ScheduleFadeOut(Window w)
    {
        var timer = new DispatcherTimer { Interval = VisibleDuration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var anim = new DoubleAnimation
            {
                From = w.Opacity,
                To = 0.0,
                Duration = FadeDuration,
            };
            anim.Completed += (_, _) =>
            {
                try { w.Close(); } catch { /* already closing */ }
            };
            w.BeginAnimation(UIElement.OpacityProperty, anim);
        };
        timer.Start();
    }

    private static Window BuildToastWindow(string title, string message, ToastKind kind)
    {
        var accent = kind switch
        {
            ToastKind.Error => Color.FromRgb(220, 80, 80),
            ToastKind.Warning => Color.FromRgb(220, 160, 60),
            _ => Color.FromRgb(80, 150, 220),
        };

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            // Start offscreen so we don't flash at (0,0) before Loaded repositions us.
            Left = -32000,
            Top = -32000,
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 32, 32, 36)),
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(0, 0, 0, 3),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 12),
            MaxWidth = 380,
            MinWidth = 220,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12,
                ShadowDepth = 2,
                Opacity = 0.45,
                Color = Colors.Black,
            },
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = new SolidColorBrush(accent),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            FontFamily = new FontFamily("Segoe UI"),
        });
        stack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = new SolidColorBrush(Color.FromRgb(225, 225, 225)),
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
        });

        border.Child = stack;
        window.Content = border;
        return window;
    }
}
