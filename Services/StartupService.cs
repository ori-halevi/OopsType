using System;
using Microsoft.Win32;
using OopsType.Infrastructure;

namespace OopsType.Services;

public sealed class StartupService : IStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OopsType";

    private readonly IErrorReporter _reporter;

    public StartupService(IErrorReporter reporter) => _reporter = reporter;

    public bool IsEnabled()
    {
        // Group Policy / managed devices can restrict access to HKCU\Software\...\Run, throwing
        // SecurityException or IOException. In those cases we treat autostart as disabled rather
        // than crashing — the user can re-tick the box manually.
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            if (key == null) return false;
            var v = key.GetValue(ValueName) as string;
            if (string.IsNullOrWhiteSpace(v)) return false;
            return string.Equals(StripQuotes(v), GetExePath(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _reporter.Report("StartupService.IsEnabled", ex);
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key == null)
            {
                _reporter.Report("StartupService.SetEnabled",
                    $"CreateSubKey returned null for HKCU\\{RunKey}.");
                return;
            }
            if (enabled)
                key.SetValue(ValueName, $"\"{GetExePath()}\"");
            else if (key.GetValue(ValueName) != null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            _reporter.Report("StartupService.SetEnabled", ex);
        }
    }

    private static string GetExePath() => Environment.ProcessPath ?? AppContext.BaseDirectory;
    private static string StripQuotes(string s) => s.Trim().Trim('"');
}
