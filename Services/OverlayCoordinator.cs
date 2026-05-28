using System;
using System.Windows;
using System.Windows.Threading;
using OopsType.Native;
using OopsType.ViewModels;
using OopsType.Views;

namespace OopsType.Services;

public sealed class OverlayCoordinator : IOverlayCoordinator
{
    private readonly ISettingsService _settings;
    private readonly IKeyboardLayoutService _layout;
    private readonly ICaretLocationService _caret;
    private readonly ITaskbarService _taskbar;
    private readonly IKeyboardActivityService _activity;

    private CaretLabelOverlay? _caretOverlay;
    private CaretLabelViewModel? _caretVm;
    private DispatcherTimer? _caretFollowTimer;

    private MouseLabelOverlay? _mouseOverlay;
    private MouseLabelViewModel? _mouseVm;
    private DispatcherTimer? _mouseFollowTimer;

    private TaskbarStripOverlay? _stripOverlay;
    private TaskbarStripViewModel? _stripVm;

    private DispatcherTimer? _heartbeatTimer;

    public OverlayCoordinator(
        ISettingsService settings,
        IKeyboardLayoutService layout,
        ICaretLocationService caret,
        ITaskbarService taskbar,
        IKeyboardActivityService activity)
    {
        _settings = settings;
        _layout = layout;
        _caret = caret;
        _taskbar = taskbar;
        _activity = activity;
    }

    public void Start()
    {
        _layout.LanguageChanged += OnLanguageChanged;
        _activity.KeyPressed += OnKeyPressed;
        _settings.Changed += ApplySettings;
        ApplySettings();

        _heartbeatTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1500),
        };
        _heartbeatTimer.Tick += (_, _) =>
        {
            _caretOverlay?.EnsureTopmost();
            _mouseOverlay?.EnsureTopmost();
            UpdateStripPosition();
        };
        _heartbeatTimer.Start();
    }

    public void ApplySettings()
    {
        ApplyCaretOverlay();
        ApplyMouseOverlay();
        ApplyStripOverlay();
    }

    private void OnLanguageChanged(Models.LanguageInfo info)
    {
        _caretVm?.Update(info);
        _mouseVm?.Update(info);
        _stripVm?.Update(info);
        UpdateStripPosition();
        UpdateCaretPosition();
        UpdateMousePosition();
    }

    private void OnKeyPressed() => UpdateCaretPosition();

    // ------- Caret overlay (follows text caret) -------
    private void ApplyCaretOverlay()
    {
        var enabled = _settings.Current.CaretLabel.Enabled;
        if (enabled && _caretOverlay == null)
        {
            _caretVm = new CaretLabelViewModel(_settings);
            _caretVm.Update(_layout.Current);
            _caretOverlay = new CaretLabelOverlay { DataContext = _caretVm };
            _caretOverlay.Show();
            _caretOverlay.Visibility = Visibility.Hidden;
            _caretOverlay.EnsureTopmost();
            UpdateCaretPosition();

            _caretFollowTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(120),
            };
            _caretFollowTimer.Tick += (_, _) => UpdateCaretPosition();
            _caretFollowTimer.Start();
        }
        else if (!enabled && _caretOverlay != null)
        {
            _caretFollowTimer?.Stop();
            _caretFollowTimer = null;
            _caretOverlay.Close();
            _caretOverlay = null;
            _caretVm = null;
        }
        else if (enabled && _caretVm != null)
        {
            _caretVm.RefreshFromSettings();
            UpdateCaretPosition();
        }
    }

    private void UpdateCaretPosition()
    {
        if (_caretOverlay == null || _caretVm == null) return;

        var info = _caret.GetCaretRect();
        if (!info.Found)
        {
            if (_caretOverlay.Visibility == Visibility.Visible)
                _caretOverlay.Visibility = Visibility.Hidden;
            return;
        }

        var s = _settings.Current.CaretLabel;
        double labelH = _caretOverlay.ActualHeight > 0 ? _caretOverlay.ActualHeight : 18;

        // Anchor: top of caret minus label height (i.e. just above the caret line). User offsets from there.
        double x = info.ScreenRect.X + s.OffsetX;
        double y = info.ScreenRect.Y - labelH + s.OffsetY;

        _caretOverlay.PositionInScreenPixels(x, y);
        if (_caretOverlay.Visibility != Visibility.Visible)
            _caretOverlay.Visibility = Visibility.Visible;
        _caretOverlay.EnsureTopmost();
    }

    // ------- Mouse overlay (follows mouse cursor) -------
    private void ApplyMouseOverlay()
    {
        var enabled = _settings.Current.MouseLabel.Enabled;
        if (enabled && _mouseOverlay == null)
        {
            _mouseVm = new MouseLabelViewModel(_settings);
            _mouseVm.Update(_layout.Current);
            _mouseOverlay = new MouseLabelOverlay { DataContext = _mouseVm };
            _mouseOverlay.Show();
            _mouseOverlay.EnsureTopmost();
            UpdateMousePosition();

            _mouseFollowTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(25),
            };
            _mouseFollowTimer.Tick += (_, _) => UpdateMousePosition();
            _mouseFollowTimer.Start();
        }
        else if (!enabled && _mouseOverlay != null)
        {
            _mouseFollowTimer?.Stop();
            _mouseFollowTimer = null;
            _mouseOverlay.Close();
            _mouseOverlay = null;
            _mouseVm = null;
        }
        else if (enabled && _mouseVm != null)
        {
            _mouseVm.RefreshFromSettings();
            UpdateMousePosition();
        }
    }

    private void UpdateMousePosition()
    {
        if (_mouseOverlay == null) return;
        if (!NativeMethods.GetCursorPos(out var p)) return;

        var s = _settings.Current.MouseLabel;
        _mouseOverlay.PositionInScreenPixels(p.X + s.OffsetX, p.Y + s.OffsetY);
        if (_mouseOverlay.Visibility != Visibility.Visible)
            _mouseOverlay.Visibility = Visibility.Visible;
    }

    // ------- Taskbar strip -------
    private void ApplyStripOverlay()
    {
        var enabled = _settings.Current.TaskbarStrip.Enabled;
        if (enabled && _stripOverlay == null)
        {
            _stripVm = new TaskbarStripViewModel(_settings);
            _stripVm.Update(_layout.Current);
            _stripOverlay = new TaskbarStripOverlay { DataContext = _stripVm };
            _stripOverlay.Show();
            _stripOverlay.EnsureTopmost();
            UpdateStripPosition();
        }
        else if (!enabled && _stripOverlay != null)
        {
            _stripOverlay.Close();
            _stripOverlay = null;
            _stripVm = null;
        }
        else if (enabled && _stripVm != null)
        {
            _stripVm.Update(_layout.Current);
            UpdateStripPosition();
        }
    }

    private void UpdateStripPosition()
    {
        if (_stripOverlay == null) return;
        if (!_taskbar.TryGetPrimaryTaskbarRect(out var r))
        {
            if (_stripOverlay.Visibility == Visibility.Visible)
                _stripOverlay.Visibility = Visibility.Hidden;
            return;
        }

        var s = _settings.Current.TaskbarStrip;
        double thickness = ResolveThickness(s.Thickness, r.Height);
        double y = (s.VerticalPosition ?? "top").Equals("bottom", StringComparison.OrdinalIgnoreCase)
            ? r.Bottom - thickness
            : r.Y;

        _stripOverlay.Opacity = s.OpacityEnabled ? Math.Clamp(s.Opacity, 0.0, 1.0) : 1.0;
        _stripOverlay.PositionInScreenPixels(r.X, y, r.Width, thickness);
        if (_stripOverlay.Visibility != Visibility.Visible)
            _stripOverlay.Visibility = Visibility.Visible;

        if ((s.Placement ?? "front").Equals("behind", StringComparison.OrdinalIgnoreCase))
            _stripOverlay.EnsureBehindTaskbar();
        else
            _stripOverlay.EnsureTopmost();
    }

    private static double ResolveThickness(string thickness, double taskbarHeight) =>
        (thickness ?? "small").ToLowerInvariant() switch
        {
            "full" => Math.Max(1, taskbarHeight),
            "large" => 16,
            "medium" => 8,
            _ => 3,
        };

    public void Shutdown()
    {
        _heartbeatTimer?.Stop();
        _caretFollowTimer?.Stop();
        _mouseFollowTimer?.Stop();
        _caretOverlay?.Close();
        _mouseOverlay?.Close();
        _stripOverlay?.Close();
        _layout.LanguageChanged -= OnLanguageChanged;
        _activity.KeyPressed -= OnKeyPressed;
        _settings.Changed -= ApplySettings;
    }
}
