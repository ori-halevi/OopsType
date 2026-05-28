using System;
using System.IO;
using System.Text.Json;
using OopsType.Models;

namespace OopsType.Services;

/// <summary>
/// JSON-backed implementation of <see cref="ISettingsService"/>. Settings live at
/// <c>%LOCALAPPDATA%\OopsType\settings.json</c>. Mutations are made on <see cref="Current"/>
/// directly; callers invoke <see cref="Save"/> to persist and broadcast a <see cref="Changed"/>
/// event so subscribers (overlays, tray) can react.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;
    private readonly ITransparencyDetector _transparency;
    private AppSettings _current;

    public AppSettings Current => _current;
    public bool IsFirstLaunch { get; }
    public event Action? Changed;

    public SettingsService(ITransparencyDetector transparency)
    {
        _transparency = transparency;

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OopsType");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");

        IsFirstLaunch = !File.Exists(_path);
        _current = Load();
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
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // Corrupt JSON shouldn't crash the app — fall back to defaults instead, which
            // will be re-persisted on the next Save().
            return new AppSettings();
        }
    }

    private void WriteToDisk(AppSettings s) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(s, JsonOptions));

    /// <summary>
    /// First-run defaults tuned to look good on the user's current Windows theme. With
    /// transparency effects ON (Win11 acrylic), sit BEHIND the taskbar with a full-height tint —
    /// the bar bleeds through the acrylic. With transparency OFF, draw a medium opaque bar at
    /// the bottom edge instead, so it's visible against the solid taskbar.
    /// </summary>
    private void ApplyFirstRunDefaults(AppSettings s)
    {
        if (_transparency.IsTransparencyEffectsEnabled())
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
