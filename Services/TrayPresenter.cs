using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace OopsType.Services;

/// <summary>
/// "View" for the system tray: owns the <c>NotifyIcon</c> and its context menu, translates user
/// clicks into <see cref="ISettingsService"/> mutations, and stays in sync with external setting
/// changes (e.g. when the user toggles a feature from the settings window).
/// </summary>
public sealed class TrayPresenter : ITrayPresenter
{
    private readonly ISettingsService _settings;
    private readonly ISettingsDialog _settingsDialog;

    /// <summary>Actions that refresh menu item check-states from current settings — invoked on every <c>settings.Changed</c>.</summary>
    private readonly List<Action> _refreshActions = new();

    private WinForms.NotifyIcon? _icon;

    public TrayPresenter(ISettingsService settings, ISettingsDialog settingsDialog)
    {
        _settings = settings;
        _settingsDialog = settingsDialog;
    }

    public void Start()
    {
        if (_icon != null) return;

        _icon = new WinForms.NotifyIcon
        {
            Visible = true,
            Text = "OopsType",
            Icon = BuildTrayIcon(),
        };
        _icon.DoubleClick += (_, _) => _settingsDialog.Show();
        _icon.ContextMenuStrip = BuildContextMenu();

        // One subscription drives every checkbox refresh instead of one handler per item.
        _settings.Changed += RefreshAllItems;
    }

    private WinForms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => _settingsDialog.Show());
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
        menu.Items.Add("Quit", null, (_, _) => Application.Current.Shutdown());

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
            Checked = getEnabled(),
        };

        item.CheckedChanged += (_, _) =>
        {
            setEnabled(item.Checked);
            _settings.Save();
        };

        _refreshActions.Add(() => item.Checked = getEnabled());
        return item;
    }

    private void RefreshAllItems()
    {
        foreach (var refresh in _refreshActions) refresh();
    }

    /// <summary>
    /// Generates a tiny 16×16 icon at runtime so we don't ship a separate .ico resource. Looks
    /// like a black square with a white "O".
    /// </summary>
    private static Icon BuildTrayIcon()
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

    public void Dispose()
    {
        if (_icon == null) return;

        _settings.Changed -= RefreshAllItems;
        _icon.Visible = false;
        _icon.Dispose();
        _icon = null;
    }
}
