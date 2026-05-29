using System;
using System.IO;
using System.Text;
using System.Text.Json;
using OopsType.Infrastructure;
using OopsType.Models;

namespace OopsType.Services;

/// <summary>
/// JSON-backed implementation of <see cref="ISettingsService"/>. Settings live at
/// <c>%LOCALAPPDATA%\OopsType\settings.json</c>. Mutations are made on <see cref="Current"/>
/// directly; callers invoke <see cref="Save"/> to persist and broadcast a <see cref="Changed"/>
/// event so subscribers (overlays, tray) can react.
///
/// <para>Persistence strategy is hardened for 24/7 / lossy power:
/// <list type="number">
///   <item>Write the new JSON to <c>settings.json.tmp</c> with <see cref="FileOptions.WriteThrough"/>
///   so the bytes are flushed to disk before we proceed (a sudden power loss between WriteAllText
///   and Move would otherwise leave a 0-byte tmp on some filesystems).</item>
///   <item>Copy the existing <c>settings.json</c> to <c>settings.json.bak</c> BEFORE the move.
///   This gives us a known-good snapshot to fall back to if the main file is later found
///   corrupt or empty (it has happened — antivirus quarantine, power cut mid-write on cheap
///   SSDs, an OOM killing us between flush and rename, …).</item>
///   <item><see cref="Load"/> tries <c>settings.json</c> first, then <c>settings.json.bak</c>,
///   then defaults — so a corrupt main never silently wipes a user's whole configuration.</item>
/// </list></para>
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

    private string BackupPath => _path + ".bak";
    private string TmpPath => _path + ".tmp";

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

        // No usable path → behave as first launch with defaults, never re-persist. The presence
        // of just the .bak isn't enough to demote first-launch: a user who deleted the main file
        // intentionally should not be ambushed by a stale backup, and if the main is corrupt
        // the Load() fallback will surface the backup anyway.
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

        // Try the primary file first.
        if (TryDeserializeFile(_path, "SettingsService.Load", out var primary))
            return primary;

        // Primary failed — fall back to the most recent .bak. This is the whole reason we keep
        // one: silently dropping the user's whole config because of a single corrupt byte in
        // the main file is unacceptable for a long-running utility.
        if (File.Exists(BackupPath) && TryDeserializeFile(BackupPath, "SettingsService.LoadBackup", out var backup))
        {
            _reporter.Report("SettingsService.LoadBackup",
                "Main settings file unreadable; loaded from .bak fallback.");
            return backup;
        }

        return new AppSettings();
    }

    private bool TryDeserializeFile(string path, string reportSource, out AppSettings result)
    {
        result = new AppSettings();
        try
        {
            var json = File.ReadAllText(path);
            // Empty/whitespace file (mid-write power loss before flush completed) — treat as a
            // load failure so the caller can try the .bak instead of accepting defaults.
            if (string.IsNullOrWhiteSpace(json)) return false;

            var parsed = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (parsed == null) return false;
            result = parsed;
            return true;
        }
        catch (Exception ex)
        {
            _reporter.Report(reportSource, ex);
            return false;
        }
    }

    /// <summary>
    /// Persists <paramref name="s"/> via backup → write-through tmp → atomic rename. Returns
    /// false on failure (already reported). Sequence:
    /// <list type="number">
    ///   <item>If a current settings.json exists, copy it to settings.json.bak (overwrite). This
    ///   is our "last known good" snapshot if the write that follows is somehow corrupted.</item>
    ///   <item>Write JSON to settings.json.tmp via <see cref="FileStream"/> with
    ///   <see cref="FileOptions.WriteThrough"/>, then explicitly Flush(true) before close — so
    ///   the bytes have hit the disk before we rename, not just the OS cache.</item>
    ///   <item><see cref="File.Move(string, string, bool)"/> tmp → settings.json. NTFS makes this
    ///   atomic on the same volume.</item>
    /// </list>
    /// </summary>
    private bool WriteToDisk(AppSettings s)
    {
        if (string.IsNullOrEmpty(_path)) return false;

        try
        {
            // Step 1: snapshot the previous good file as .bak.
            if (File.Exists(_path))
            {
                try { File.Copy(_path, BackupPath, overwrite: true); }
                catch (Exception ex) { _reporter.Report("SettingsService.Backup", ex); }
            }

            // Step 2: write tmp with WriteThrough + explicit fsync.
            var json = JsonSerializer.Serialize(s, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            using (var fs = new FileStream(
                TmpPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.WriteThrough))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }

            // Step 3: atomic rename onto the real path.
            File.Move(TmpPath, _path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _reporter.Report("SettingsService.Save", ex);
            // Best-effort tmp cleanup — leaving a stale .tmp around isn't catastrophic but it
            // would confuse the next Save (Create truncates anyway) so we tidy when we can.
            try { if (File.Exists(TmpPath)) File.Delete(TmpPath); } catch { }
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
