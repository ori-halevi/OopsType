using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using OopsType.Infrastructure;
using OopsType.Services.Localization;
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
    private readonly ILocalizationService _localization;

    /// <summary>Actions that refresh menu item check-states from current settings — invoked on every <c>settings.Changed</c>.</summary>
    private readonly List<Action> _refreshActions = new();

    /// <summary>Actions that re-read translations into menu item text — invoked on every <c>localization.LanguageChanged</c>.</summary>
    private readonly List<Action> _retranslateActions = new();

    private WinForms.NotifyIcon? _icon;
    // Tracked separately so Dispose can release the GDI/native handles each of these owns —
    // NotifyIcon.Dispose does NOT cascade to its Icon, ContextMenuStrip, or the Font we attached.
    // For 24/7 use, the per-run cost is small, but it matters when (a) the app is restarted by a
    // session-recycling autostart wrapper, (b) the user toggles a hypothetical future re-init, or
    // (c) just because leaking GDI handles is a smell that hides real leaks later.
    private Icon? _ownedIcon;
    private WinForms.ContextMenuStrip? _ownedMenu;
    private Font? _ownedMenuFont;

    public TrayPresenter(ISettingsService settings, ISettingsDialog settingsDialog, IErrorReporter reporter, IToastService toasts, ILocalizationService localization)
    {
        _settings = settings;
        _settingsDialog = settingsDialog;
        _reporter = reporter;
        _toasts = toasts;
        _localization = localization;
    }

    public void Start()
    {
        if (_icon != null) return;

        try
        {
            _ownedIcon = BuildTrayIcon();
            _icon = new WinForms.NotifyIcon
            {
                Visible = true,
                Text = "OopsType",
                Icon = _ownedIcon,
            };
            _icon.DoubleClick += OnDoubleClick;
            _ownedMenu = BuildContextMenu();
            _icon.ContextMenuStrip = _ownedMenu;

            // One subscription drives every checkbox refresh instead of one handler per item.
            _settings.Changed += RefreshAllItems;
            _localization.LanguageChanged += RetranslateAllItems;
            _reporter.Notified += OnErrorReported;
        }
        catch (Exception ex)
        {
            _reporter.Report("TrayPresenter.Start", ex);
            // Roll back so a partially-constructed icon doesn't cause Dispose issues.
            try { _icon?.Dispose(); } catch { }
            try { _ownedMenu?.Dispose(); } catch { }
            try { _ownedMenuFont?.Dispose(); } catch { }
            try { _ownedIcon?.Dispose(); } catch { }
            _icon = null;
            _ownedMenu = null;
            _ownedMenuFont = null;
            _ownedIcon = null;
        }
    }

    private void OnDoubleClick(object? sender, EventArgs e) =>
        Safe.Invoke(_reporter, "TrayPresenter.DoubleClick", _settingsDialog.Show);

    private WinForms.ContextMenuStrip BuildContextMenu()
    {
        // Track the Font separately so Dispose can release it — ContextMenuStrip doesn't take
        // ownership of a Font you assign through the property setter.
        _ownedMenuFont = new Font("Segoe UI Variable Text", 9.5f, System.Drawing.FontStyle.Regular, GraphicsUnit.Point);
        var menu = new WinForms.ContextMenuStrip
        {
            Renderer = new FluentMenuRenderer(),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Font = _ownedMenuFont,
            BackColor = FluentMenuPalette.Background,
            ForeColor = FluentMenuPalette.Text,
            Padding = new Padding(4),
        };

        AddTranslatedItem(menu, "Tray_OpenSettings", (_, _) =>
            Safe.Invoke(_reporter, "TrayPresenter.OpenSettings", _settingsDialog.Show));
        menu.Items.Add(new WinForms.ToolStripSeparator());

        menu.Items.Add(BuildToggleItem(
            "Tray_Toggle_Caret",
            () => _settings.Current.CaretLabel.Enabled,
            v => _settings.Current.CaretLabel.Enabled = v));

        menu.Items.Add(BuildToggleItem(
            "Tray_Toggle_Mouse",
            () => _settings.Current.MouseLabel.Enabled,
            v => _settings.Current.MouseLabel.Enabled = v));

        menu.Items.Add(BuildToggleItem(
            "Tray_Toggle_Strip",
            () => _settings.Current.TaskbarStrip.Enabled,
            v => _settings.Current.TaskbarStrip.Enabled = v));

        menu.Items.Add(BuildToggleItem(
            "Tray_Toggle_Idle",
            () => _settings.Current.IdleReset.Enabled,
            v => _settings.Current.IdleReset.Enabled = v));

        menu.Items.Add(new WinForms.ToolStripSeparator());
        AddTranslatedItem(menu, "Tray_Quit", (_, _) =>
            Safe.Invoke(_reporter, "TrayPresenter.Quit", () => System.Windows.Application.Current?.Shutdown()));

        return menu;
    }

    /// <summary>
    /// Adds a non-toggle menu item whose text is sourced from the localization key and refreshed
    /// every time <see cref="ILocalizationService.LanguageChanged"/> fires. Kept here (rather
    /// than inlined) so every plain menu entry hooks the retranslate path uniformly.
    /// </summary>
    private void AddTranslatedItem(WinForms.ContextMenuStrip menu, string key, EventHandler onClick)
    {
        var item = new WinForms.ToolStripMenuItem(_localization.T(key));
        item.Click += onClick;
        menu.Items.Add(item);
        _retranslateActions.Add(() => Safe.Invoke(_reporter, "TrayPresenter.RetranslateItem",
            () => item.Text = _localization.T(key)));
    }

    /// <summary>
    /// Factory for a checkable menu item bound to a boolean setting. The first parameter is a
    /// localization key — the item's text is sourced from the active language pack and refreshed
    /// when the language changes. The getter is captured for cheap settings-change refresh, and
    /// the setter for click → update → persist in one place.
    /// </summary>
    private WinForms.ToolStripMenuItem BuildToggleItem(string textKey, Func<bool> getEnabled, Action<bool> setEnabled)
    {
        var item = new WinForms.ToolStripMenuItem(_localization.T(textKey))
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
        _retranslateActions.Add(() => Safe.Invoke(_reporter, "TrayPresenter.RetranslateItem",
            () => item.Text = _localization.T(textKey)));
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

    private void RetranslateAllItems()
    {
        foreach (var retranslate in _retranslateActions) retranslate();
    }

    private void OnErrorReported(ErrorNotification n)
    {
        // IToastService marshals to the UI thread itself; we just forward. The balloon path was
        // dropped because Win10/11 routes ShowBalloonTip through Action Center, which silences
        // it under common defaults — making the notification unreliable for debugging.
        var text = string.IsNullOrEmpty(n.Message) ? n.Source : $"{n.Source}\n{n.Message}";
        _toasts.Show(_localization.T("Tray_ErrorToast_Title"), Truncate(text, 400), ToastKind.Error);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    /// <summary>
    /// Loads the shipped logo .ico from embedded WPF resources. Falls back to null on failure
    /// so <see cref="WinForms.NotifyIcon"/> uses a system default instead of crashing.
    /// </summary>
    private static Icon? BuildTrayIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/logo.ico", UriKind.Absolute);
            using var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            return stream != null ? new Icon(stream) : null;
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
        try { _localization.LanguageChanged -= RetranslateAllItems; } catch { }
        try { _icon.Visible = false; } catch { }
        try { _icon.Dispose(); } catch { }
        // ContextMenuStrip first (it references the Font), then Font, then Icon. Each in its own
        // try so one stuck handle can't strand the others.
        try { _ownedMenu?.Dispose(); } catch { }
        try { _ownedMenuFont?.Dispose(); } catch { }
        try { _ownedIcon?.Dispose(); } catch { }
        _icon = null;
        _ownedMenu = null;
        _ownedMenuFont = null;
        _ownedIcon = null;
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
