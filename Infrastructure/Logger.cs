using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace OopsType.Infrastructure;

/// <summary>
/// File logger at <c>%LOCALAPPDATA%\OopsType\logs\app.log</c>. Thread-safe, rotates a bounded
/// ring of files at <see cref="MaxBytesPerFile"/>, and never throws — any I/O failure is silently
/// dropped because the logger is called from inside catch blocks (re-throwing would mask the
/// real error). Also mirrors to <see cref="Debug.WriteLine"/> so an attached debugger sees every line.
///
/// <para>24/7 stability:
/// <list type="bullet">
///   <item>UTC timestamps (with Z suffix) — DST transitions don't duplicate or skip log hours.</item>
///   <item>Bounded-ring rotation: app.log → app.log.1 → app.log.2 → app.log.3 → discarded. Hard
///   cap ≈ <see cref="MaxBytesPerFile"/> × <see cref="KeptHistoricFiles"/>+1 on disk total.</item>
///   <item>Roll-failure backoff: when a rename fails (file locked by AV, indexer, etc.) we don't
///   retry on every subsequent write — we wait <see cref="RollFailureCooldown"/> before trying
///   again, so a held lock can't turn into a write storm.</item>
///   <item>Per-(level,source,message) coalescing: a repeating error in a per-frame handler can't
///   carpet-bomb the disk — duplicates within <see cref="CoalesceWindow"/> are collapsed and the
///   next allowed write carries a "(N earlier suppressed)" tail.</item>
/// </list></para>
/// </summary>
public sealed class Logger : ILogger
{
    private const long MaxBytesPerFile = 1 * 1024 * 1024;
    private const int KeptHistoricFiles = 3;
    private static readonly TimeSpan RollFailureCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(2);

    // Cap on the coalesce map. Source labels are developer-defined static strings so this is
    // bounded in practice, but a defensive evict-when-large keeps a future refactor that
    // introduces dynamic source labels from quietly leaking memory.
    private const int MaxCoalesceEntries = 512;

    private readonly object _gate = new();
    private readonly string _path;
    private readonly Dictionary<string, CoalesceEntry> _coalesce =
        new(StringComparer.Ordinal);

    private long _lastFailedRollTicks;

    public Logger()
    {
        // Best-effort path resolution. If folder creation fails (permissions, disk full),
        // we end up with an empty path and the file sink no-ops — Debug output still works.
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OopsType", "logs");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "app.log");
        }
        catch
        {
            _path = string.Empty;
        }
    }

    public void Error(string source, Exception ex) => Write("ERROR", source, ex.ToString());
    public void Warn(string source, string message) => Write("WARN", source, message);
    public void Info(string message) => Write("INFO", string.Empty, message);

    private void Write(string level, string source, string message)
    {
        // Stable, low-cost identity for coalescing. We hash the head of the message so a long
        // stack trace doesn't bloat the key. The level+source prefix already gives strong
        // separation between unrelated lines that happen to share a head.
        var head = message.Length <= 96 ? message : message.AsSpan(0, 96).ToString();
        var coalesceKey = level + "|" + source + "|" + head;

        int suppressedTail;
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            if (_coalesce.TryGetValue(coalesceKey, out var entry)
                && Stopwatch.GetElapsedTime(entry.LastWriteTicks) < CoalesceWindow)
            {
                entry.SuppressedSinceLast++;
                _coalesce[coalesceKey] = entry;
                // Mirror to debugger only — Debug listeners are throttled by the debugger.
                try { Debug.WriteLine($"[suppressed] {level} {source} {head}"); } catch { }
                return;
            }

            suppressedTail = entry.SuppressedSinceLast;
            _coalesce[coalesceKey] = new CoalesceEntry { LastWriteTicks = now, SuppressedSinceLast = 0 };

            if (_coalesce.Count > MaxCoalesceEntries)
                PurgeStaleCoalesceEntriesLocked();
        }

        var line = string.IsNullOrEmpty(source)
            ? $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} [{level}] {message}"
            : $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} [{level}] [{source}] {message}";

        if (suppressedTail > 0)
            line += $"  (+{suppressedTail} earlier identical suppressed)";

        // Best-effort debugger mirror; can't fail in practice but defended anyway.
        try { Debug.WriteLine(line); } catch { }

        if (string.IsNullOrEmpty(_path)) return;

        try
        {
            lock (_gate)
            {
                RollIfTooLargeLocked();
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Disk full, AV lock, transient sharing violation — drop the line rather than crash.
        }
    }

    private void RollIfTooLargeLocked()
    {
        // Respect a roll-failure cooldown: if a previous rotation just failed (e.g., an indexer
        // is holding app.log.1 open), don't keep retrying on every write — that converts a
        // single lock contention into a sustained I/O storm.
        if (_lastFailedRollTicks != 0
            && Stopwatch.GetElapsedTime(_lastFailedRollTicks) < RollFailureCooldown)
            return;

        try
        {
            var fi = new FileInfo(_path);
            if (!fi.Exists || fi.Length < MaxBytesPerFile) return;

            // Shift the ring: app.log.{N-1} dies, .{N-2} → .{N-1}, ..., .1 → .2, .log → .1.
            // Doing it from oldest to newest is critical so we never overwrite live data.
            var oldest = _path + "." + KeptHistoricFiles;
            if (File.Exists(oldest)) File.Delete(oldest);

            for (int i = KeptHistoricFiles - 1; i >= 1; i--)
            {
                var src = _path + "." + i;
                var dst = _path + "." + (i + 1);
                if (File.Exists(src)) File.Move(src, dst);
            }

            File.Move(_path, _path + ".1");
            _lastFailedRollTicks = 0;
        }
        catch
        {
            // Roll failed — remember when, so the cooldown kicks in. Caller still appends to the
            // (now oversized) current file rather than dropping the line.
            _lastFailedRollTicks = Stopwatch.GetTimestamp();
        }
    }

    private void PurgeStaleCoalesceEntriesLocked()
    {
        // Drop entries that haven't been touched within the coalesce window — they can't suppress
        // anything anymore. Keeps the dictionary bounded under any conceivable source-label churn.
        var stale = new List<string>();
        foreach (var (key, entry) in _coalesce)
            if (Stopwatch.GetElapsedTime(entry.LastWriteTicks) > CoalesceWindow)
                stale.Add(key);
        foreach (var k in stale) _coalesce.Remove(k);
    }

    private struct CoalesceEntry
    {
        public long LastWriteTicks;
        public int SuppressedSinceLast;
    }
}
