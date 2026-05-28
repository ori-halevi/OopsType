using System;
using System.IO;
using System.Text.Json;
using OopsType.Infrastructure;
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
    private readonly IErrorReporter _reporter;
    private AppSettings _current;

    public AppSettings Current => _current;
    public bool IsFirstLaunch { get; }
    public event Action? Changed;

    public SettingsService(ITransparencyDetector transparency, IErrorReporter reporter)
    {
        _transparency = transparency;
        _reporter = reporter;

        // Directory creation can fail (permissions, disk full, weird roaming profile). In that
        // case we fall back to an in-memory-only settings path: Save will silently no-op, but the
        // app still runs with defaults instead of refusing to start.
        string path;
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OopsType");
            Directory.CreateDirectory(dir);
            path = Path.Combine(dir, "settings.json");
        }
        catch (Exception ex)
        {
            _reporter.Report("SettingsService.Init", ex);
            path = string.Empty;
        }
        _path = path;

        // No usable path → behave as first launch with defaults, never re-persist.
        IsFirstLaunch = string.IsNullOrEmpty(_path) || !File.Exists(_path);
        _current = Load();
    }

    public void Save()
    {
        if (!WriteToDisk(_current)) return;

        // A buggy listener (e.g. a presenter throwing during ApplySettings) must not poison the
        // save path — the file is already on disk regardless.
        try { Changed?.Invoke(); }
        catch (Exception ex) { _reporter.Report("SettingsService.Changed", ex); }
    }

    public void Reload()
    {
        _current = Load();
        try { Changed?.Invoke(); }
        catch (Exception ex) { _reporter.Report("SettingsService.Changed", ex); }
    }

    private AppSettings Load()
    {
        if (IsFirstLaunch)
        {
            var fresh = new AppSettings();
            ApplyFirstRunDefaults(fresh);
            // Persist immediately so closing the settings window with X never loses defaults.
            // WriteToDisk silently no-ops if _path is empty.
            WriteToDisk(fresh);
            return fresh;
        }

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            // Corrupt JSON or transient I/O — fall back to defaults rather than crash. The next
            // Save() will overwrite the bad file with valid JSON.
            _reporter.Report("SettingsService.Load", ex);
            return new AppSettings();
        }
    }

    /// <summary>
    /// Writes <paramref name="s"/> via a tmp-then-rename so a crash mid-write can't leave a
    /// truncated/empty settings.json. Returns false on failure (already reported).
    /// </summary>
    private bool WriteToDisk(AppSettings s)
    {
        if (string.IsNullOrEmpty(_path)) return false;

        try
        {
            var json = JsonSerializer.Serialize(s, JsonOptions);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            // File.Move with overwrite is atomic on the same volume (NTFS). If something deletes
            // the .tmp between WriteAllText and Move, we fall through to the catch.
            File.Move(tmp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _reporter.Report("SettingsService.Save", ex);
            return false;
        }
    }

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
