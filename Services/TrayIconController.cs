using System;
using System.Drawing;
using System.Windows;
using OopsType.Views;
using WinForms = System.Windows.Forms;

namespace OopsType.Services;

public sealed class TrayIconController : IDisposable
{
    private readonly Func<SettingsWindow> _settingsFactory;
    private readonly ISettingsService _settings;
    private readonly IOverlayCoordinator _overlays;
    private WinForms.NotifyIcon? _icon;

    public TrayIconController(Func<SettingsWindow> settingsFactory, ISettingsService settings, IOverlayCoordinator overlays)
    {
        _settingsFactory = settingsFactory;
        _settings = settings;
        _overlays = overlays;
    }

    public void Start()
    {
        _icon = new WinForms.NotifyIcon
        {
            Visible = true,
            Text = "OopsType",
            Icon = BuildIcon(),
        };
        _icon.DoubleClick += (_, _) => OpenSettings();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var caret = new WinForms.ToolStripMenuItem("Caret label") { CheckOnClick = true };
        caret.Checked = _settings.Current.CaretLabel.Enabled;
        caret.CheckedChanged += (_, _) => { _settings.Current.CaretLabel.Enabled = caret.Checked; _settings.Save(); };
        menu.Items.Add(caret);

        var mouse = new WinForms.ToolStripMenuItem("Mouse label") { CheckOnClick = true };
        mouse.Checked = _settings.Current.MouseLabel.Enabled;
        mouse.CheckedChanged += (_, _) => { _settings.Current.MouseLabel.Enabled = mouse.Checked; _settings.Save(); };
        menu.Items.Add(mouse);

        var strip = new WinForms.ToolStripMenuItem("Taskbar strip") { CheckOnClick = true };
        strip.Checked = _settings.Current.TaskbarStrip.Enabled;
        strip.CheckedChanged += (_, _) => { _settings.Current.TaskbarStrip.Enabled = strip.Checked; _settings.Save(); };
        menu.Items.Add(strip);

        var idle = new WinForms.ToolStripMenuItem("Idle reset") { CheckOnClick = true };
        idle.Checked = _settings.Current.IdleReset.Enabled;
        idle.CheckedChanged += (_, _) => { _settings.Current.IdleReset.Enabled = idle.Checked; _settings.Save(); };
        menu.Items.Add(idle);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Application.Current.Shutdown());

        _settings.Changed += () =>
        {
            caret.Checked = _settings.Current.CaretLabel.Enabled;
            mouse.Checked = _settings.Current.MouseLabel.Enabled;
            strip.Checked = _settings.Current.TaskbarStrip.Enabled;
            idle.Checked = _settings.Current.IdleReset.Enabled;
        };

        _icon.ContextMenuStrip = menu;
    }

    private void OpenSettings()
    {
        var existing = false;
        foreach (Window w in Application.Current.Windows)
        {
            if (w is SettingsWindow sw)
            {
                sw.Activate();
                existing = true;
                break;
            }
        }
        if (!existing)
        {
            var win = _settingsFactory();
            win.Show();
            win.Activate();
        }
    }

    private static Icon BuildIcon()
    {
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.FillRectangle(Brushes.Black, 0, 0, 16, 16);
            using var font = new Font("Segoe UI", 7f, System.Drawing.FontStyle.Bold);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("O", font, Brushes.White, new RectangleF(0, 0, 16, 16), sf);
        }
        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void Dispose()
    {
        if (_icon != null)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}
