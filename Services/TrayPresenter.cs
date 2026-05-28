using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
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
        var menu = new WinForms.ContextMenuStrip();
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
            Safe.Invoke(_reporter, "TrayPresenter.Quit", () => Application.Current?.Shutdown()));

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
