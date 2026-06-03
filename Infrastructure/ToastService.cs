using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    public void Show(string title, string message, ToastKind kind, ToastCopyAction? copy = null)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => Show(title, message, kind, copy)));
            return;
        }

        try
        {
            var w = BuildToastWindow(title, message, kind, copy);
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

        // Pause auto-dismiss while the pointer is over the toast: cancel any in-flight fade, snap
        // back to full opacity, and hold. This keeps an interactive toast (Copy button) reachable
        // — without it, a 5s window is easy to miss. The fade restarts when the pointer leaves.
        w.MouseEnter += (_, _) =>
        {
            timer.Stop();
            w.BeginAnimation(UIElement.OpacityProperty, null);
            w.Opacity = 1.0;
        };
        w.MouseLeave += (_, _) => timer.Start();

        timer.Start();
    }

    private static Window BuildToastWindow(string title, string message, ToastKind kind, ToastCopyAction? copy)
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

        if (copy != null)
            stack.Children.Add(BuildCopyButton(copy));

        border.Child = stack;
        window.Content = border;
        return window;
    }

    /// <summary>
    /// A self-styled, clipboard-writing button matching the toast's dark theme. Implemented as a
    /// clickable <see cref="Border"/> rather than a <see cref="Button"/> to avoid the system chrome
    /// (light background, blue hover) that fights the toast palette. The host toast is intentionally
    /// non-activating/non-focusable, so keyboard access isn't a concern here — mouse input still
    /// routes to child elements regardless of window activation.
    /// </summary>
    private static UIElement BuildCopyButton(ToastCopyAction copy)
    {
        var idleBg = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
        var hoverBg = new SolidColorBrush(Color.FromArgb(72, 255, 255, 255));
        var fg = new SolidColorBrush(Color.FromRgb(225, 225, 225));

        var glyph = new TextBlock
        {
            // Segoe MDL2 Assets "Copy" glyph — shipped on Win10/11.
            Text = "\uE8C8",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 11,
            Foreground = fg,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new TextBlock
        {
            Text = copy.Label,
            FontSize = 11.5,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = fg,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0, 0, 0),
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(glyph);
        content.Children.Add(label);

        var button = new Border
        {
            Child = content,
            Background = idleBg,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 9, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = Cursors.Hand,
        };

        button.MouseEnter += (_, _) => button.Background = hoverBg;
        button.MouseLeave += (_, _) => button.Background = idleBg;
        button.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                // copy: true flushes the data so it survives once the toast (and its window) closes.
                Clipboard.SetDataObject(copy.Text, true);
                label.Text = copy.CopiedLabel;

                // Revert the caption so a lingering toast can be copied again with clear feedback.
                var revert = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                revert.Tick += (_, _) => { revert.Stop(); label.Text = copy.Label; };
                revert.Start();
            }
            catch
            {
                // Clipboard can be momentarily locked by another process. Best-effort: the error is
                // still on screen and in the log, so a failed copy is non-fatal.
            }
        };

        return button;
    }
}
