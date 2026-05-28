using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace OopsType.Infrastructure;

/// <summary>
/// File logger at <c>%LOCALAPPDATA%\OopsType\logs\app.log</c>. Thread-safe, rolls once at
/// <see cref="MaxBytes"/>, and never throws — any I/O failure is silently dropped because
/// the logger is called from inside catch blocks (re-throwing would mask the real error).
/// Also mirrors to <see cref="Debug.WriteLine"/> so an attached debugger sees every line.
/// </summary>
public sealed class Logger : ILogger
{
    private const long MaxBytes = 1 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly string _path;

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
        var line = string.IsNullOrEmpty(source)
            ? $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}"
            : $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{source}] {message}";

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
        try
        {
            var fi = new FileInfo(_path);
            if (!fi.Exists || fi.Length < MaxBytes) return;

            var backup = _path + ".bak";
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(_path, backup);
        }
        catch
        {
            // Roll is opportunistic — if it fails we keep appending to the oversize file.
        }
    }
}
