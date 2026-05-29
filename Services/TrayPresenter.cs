using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using OopsType.Infrastructure;
using WinForms = System.Windows.Forms;

namespace OopsType.Services;

/// <summary>
/// "View" for the system tray: owns the <c>NotifyIcon</c> and its context menu, translates user
/// clicks into <see cref="ISettingsService"/> mutations, and stays in sync with external setting
/// changes (e.g. when the user toggles a feature from the settings window). Also surfaces
/// <see cref="IErrorReporter"/> notifications as balloon tips so silent failures aren't invisible.
/// </summary>
public sealed class TrayPresenter : ITrayPresenter
{
    private readonly ISettingsService _settings;
    private readonly ISettingsDialog _settingsDialog;
    private readonly IErrorReporter _reporter;
    private readonly IToastService _toasts;

    /// <summary>Actions that refresh menu item check-states from current settings — invoked on every <c>settings.Changed</c>.</summary>
    private readonly List<Action> _refreshActions = new();

    private WinForms.NotifyIcon? _icon;

    public TrayPresenter(ISettingsService settings, ISettingsDialog settingsDialog, IErrorReporter reporter, IToastService toasts)
    {
        _settings = settings;
        _settingsDialog = settingsDialog;
        _reporter = reporter;
        _toasts = toasts;
    }

    public void Start()
    {
        if (_icon != null) return;

        try
        {
            _icon = new WinForms.NotifyIcon
            {
                Visible = true,
                Text = "OopsType",
                Icon = BuildTrayIcon(),
            };
            _icon.DoubleClick += OnDoubleClick;
            _icon.ContextMenuStrip = BuildContextMenu();

            // One subscription drives every checkbox refresh instead of one handler per item.
            _settings.Changed += RefreshAllItems;
            _reporter.Notified += OnErrorReported;
        }
        catch (Exception ex)
        {
            _reporter.Report("TrayPresenter.Start", ex);
            // Roll back so a partially-constructed icon doesn't cause Dispose issues.
            try { _icon?.Dispose(); } catch { }
            _icon = null;
        }
    }

    private void OnDoubleClick(object? sender, EventArgs e) =>
        Safe.Invoke(_reporter, "TrayPresenter.DoubleClick", _settingsDialog.Show);

    private WinForms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new WinForms.ContextMenuStrip
        {
            Renderer = new FluentMenuRenderer(),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Font = new Font("Segoe UI Variable Text", 9.5f, System.Drawing.FontStyle.Regular, GraphicsUnit.Point),
            BackColor = FluentMenuPalette.Background,
            ForeColor = FluentMenuPalette.Text,
            Padding = new Padding(4),
        };
        menu.Items.Add("Settings…", null, (_, _) =>
            Safe.Invoke(_reporter, "TrayPresenter.OpenSettings", _settingsDialog.Show));
        menu.Items.Add(new WinForms.ToolStripSeparator());

        menu.Items.Add(BuildToggleItem(
            "Caret label",
            () => _settings.Current.CaretLabel.Enabled,
            v => _settings.Current.CaretLabel.Enabled = v));

        menu.Items.Add(BuildToggleItem(
            "Mouse label",
            () => _settings.Current.MouseLabel.Enabled,
            v => _settings.Current.MouseLabel.Enabled = v));

        menu.Items.Add(BuildToggleItem(
            "Taskbar strip",
            () => _settings.Current.TaskbarStrip.Enabled,
            v => _settings.Current.TaskbarStrip.Enabled = v));

        menu.Items.Add(BuildToggleItem(
            "Idle reset",
            () => _settings.Current.IdleReset.Enabled,
            v => _settings.Current.IdleReset.Enabled = v));

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) =>
            Safe.Invoke(_reporter, "TrayPresenter.Quit", () => System.Windows.Application.Current?.Shutdown()));

        return menu;
    }

    /// <summary>
    /// Factory for a checkable menu item bound to a boolean setting. Captures the getter so this
    /// item can be refreshed cheaply on settings change, and the setter so a user click updates
    /// and persists the setting in one place.
    /// </summary>
    private WinForms.ToolStripMenuItem BuildToggleItem(string text, Func<bool> getEnabled, Action<bool> setEnabled)
    {
        var item = new WinForms.ToolStripMenuItem(text)
        {
            CheckOnClick = true,
            Checked = SafeGet(getEnabled, false),
        };

        item.CheckedChanged += (_, _) => Safe.Invoke(_reporter, "TrayPresenter.Toggle", () =>
        {
            setEnabled(item.Checked);
            _settings.Save();
        });

        _refreshActions.Add(() => Safe.Invoke(_reporter, "TrayPresenter.RefreshItem",
            () => item.Checked = getEnabled()));
        return item;
    }

    private bool SafeGet(Func<bool> get, bool fallback)
    {
        try { return get(); }
        catch (Exception ex) { _reporter.Report("TrayPresenter.SafeGet", ex); return fallback; }
    }

    private void RefreshAllItems()
    {
        foreach (var refresh in _refreshActions) refresh();
    }

    private void OnErrorReported(ErrorNotification n)
    {
        // IToastService marshals to the UI thread itself; we just forward. The balloon path was
        // dropped because Win10/11 routes ShowBalloonTip through Action Center, which silences
        // it under common defaults — making the notification unreliable for debugging.
        var text = string.IsNullOrEmpty(n.Message) ? n.Source : $"{n.Source}\n{n.Message}";
        _toasts.Show("OopsType — error", Truncate(text, 400), ToastKind.Error);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    /// <summary>
    /// Generates a tiny 16×16 icon at runtime so we don't ship a separate .ico resource. Looks
    /// like a black square with a white "O". Returns null on failure — NotifyIcon tolerates that
    /// and falls back to a system default.
    /// </summary>
    private static Icon? BuildTrayIcon()
    {
        try
        {
            using var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillRectangle(Brushes.Black, 0, 0, 16, 16);
                using var font = new Font("Segoe UI", 7f, System.Drawing.FontStyle.Bold);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("O", font, Brushes.White, new RectangleF(0, 0, 16, 16), sf);
            }
            // Icon.FromHandle keeps a reference to the HICON; the Bitmap can be safely disposed.
            return Icon.FromHandle(bmp.GetHicon());
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_icon == null) return;

        try { _reporter.Notified -= OnErrorReported; } catch { }
        try { _settings.Changed -= RefreshAllItems; } catch { }
        try { _icon.Visible = false; } catch { }
        try { _icon.Dispose(); } catch { }
        _icon = null;
    }
}

/// <summary>
/// Fluent-leaning color palette for the tray context menu. Kept in one place so the renderer
/// and the menu itself stay in sync; nudge the values here to retune the entire menu look.
/// </summary>
internal static class FluentMenuPalette
{
    public static readonly Color Background = Color.FromArgb(248, 249, 251);
    public static readonly Color Text = Color.FromArgb(31, 41, 55);
    public static readonly Color SubtleText = Color.FromArgb(107, 114, 128);
    public static readonly Color Hover = Color.FromArgb(229, 233, 240);
    public static readonly Color HoverChecked = Color.FromArgb(214, 224, 245);
    public static readonly Color Checked = Color.FromArgb(224, 234, 252);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color Border = Color.FromArgb(34, 209, 213, 219);
    public static readonly Color Separator = Color.FromArgb(229, 231, 235);
}

/// <summary>
/// Flat, modern renderer for <see cref="ToolStripDropDownMenu"/>. Replaces the bevelled XP-era look
/// of <see cref="ToolStripProfessionalRenderer"/> with squared, low-contrast surfaces and a thin
/// accent-colored check mark — matches the FluentWindow settings UI visually.
/// </summary>
internal sealed class FluentMenuRenderer : ToolStripProfessionalRenderer
{
    public FluentMenuRenderer() : base(new FluentColorTable()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Force the text color so the base class doesn't fall back to SystemColors on disabled items.
        e.TextColor = e.Item.Enabled ? FluentMenuPalette.Text : FluentMenuPalette.SubtleText;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // Draw a clean check mark in the accent color instead of the default 16x16 system bitmap.
        var bounds = e.ImageRectangle;
        using var pen = new Pen(FluentMenuPalette.Accent, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var g = e.Graphics;
        var prev = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var x = bounds.Left + bounds.Width * 0.18f;
        var y = bounds.Top + bounds.Height * 0.52f;
        g.DrawLines(pen, new[]
        {
            new PointF(x, y),
            new PointF(bounds.Left + bounds.Width * 0.42f, bounds.Top + bounds.Height * 0.74f),
            new PointF(bounds.Left + bounds.Width * 0.82f, bounds.Top + bounds.Height * 0.30f),
        });
        g.SmoothingMode = prev;
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        // Thin, low-contrast separator; the base draws a heavier two-tone line that looks dated.
        var r = e.Item.Bounds;
        using var pen = new Pen(FluentMenuPalette.Separator, 1f);
        var y = r.Top + r.Height / 2;
        e.Graphics.DrawLine(pen, r.Left + 8, y, r.Right - 8, y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // Replace the default bevel border with a single subtle stroke aligned to the menu.
        var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        using var pen = new Pen(FluentMenuPalette.Border, 1f);
        e.Graphics.DrawRectangle(pen, r);
    }
}

/// <summary>Color table that drives <see cref="ToolStripProfessionalRenderer"/> for the tray menu.</summary>
internal sealed class FluentColorTable : ProfessionalColorTable
{
    public override Color MenuItemSelected => FluentMenuPalette.Hover;
    public override Color MenuItemSelectedGradientBegin => FluentMenuPalette.Hover;
    public override Color MenuItemSelectedGradientEnd => FluentMenuPalette.Hover;
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemPressedGradientBegin => FluentMenuPalette.HoverChecked;
    public override Color MenuItemPressedGradientEnd => FluentMenuPalette.HoverChecked;
    public override Color MenuBorder => FluentMenuPalette.Border;
    public override Color ToolStripDropDownBackground => FluentMenuPalette.Background;
    public override Color ImageMarginGradientBegin => FluentMenuPalette.Background;
    public override Color ImageMarginGradientMiddle => FluentMenuPalette.Background;
    public override Color ImageMarginGradientEnd => FluentMenuPalette.Background;
    public override Color CheckBackground => FluentMenuPalette.Checked;
    public override Color CheckSelectedBackground => FluentMenuPalette.HoverChecked;
    public override Color CheckPressedBackground => FluentMenuPalette.HoverChecked;
    public override Color SeparatorDark => FluentMenuPalette.Separator;
    public override Color SeparatorLight => FluentMenuPalette.Separator;
}
