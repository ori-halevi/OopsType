using System;
using System.IO;
using System.Text.Json;
using OopsType.Models;

namespace OopsType.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;
    private AppSettings _current;

    public AppSettings Current => _current;
    public bool IsFirstLaunch { get; }
    public event Action? Changed;

    public SettingsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OopsType");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        IsFirstLaunch = !File.Exists(_path);
        _current = Load();
    }

    private AppSettings Load()
    {
        if (IsFirstLaunch)
        {
            var fresh = new AppSettings();
            ApplyFirstRunDefaults(fresh);
            // Persist immediately so closing the settings window with X never loses defaults.
            WriteToDisk(fresh);
            return fresh;
        }
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        WriteToDisk(_current);
        Changed?.Invoke();
    }

    public void Reload()
    {
        _current = Load();
        Changed?.Invoke();
    }

    private void WriteToDisk(AppSettings s) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(s, JsonOpts));

    /// <summary>
    /// On first run, tune the taskbar strip defaults to whatever looks good on the user's
    /// current Windows theme. If transparency effects are ON (Win11 acrylic), sit BEHIND
    /// the taskbar with a full-height opaque tint. If OFF, draw a medium opaque bar in
    /// front of the taskbar, anchored at the bottom edge.
    /// </summary>
    private static void ApplyFirstRunDefaults(AppSettings s)
    {
        if (WindowsTransparencyDetector.IsTransparencyEffectsEnabled())
        {
            s.TaskbarStrip.Placement = "behind";
            s.TaskbarStrip.Thickness = "full";
            s.TaskbarStrip.OpacityEnabled = false;
        }
        else
        {
            s.TaskbarStrip.Placement = "front";
            s.TaskbarStrip.Thickness = "medium";
            s.TaskbarStrip.VerticalPosition = "bottom";
            s.TaskbarStrip.OpacityEnabled = false;
        }
    }
}
