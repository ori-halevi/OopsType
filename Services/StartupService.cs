using System;
using Microsoft.Win32;

namespace OopsType.Services;

public sealed class StartupService : IStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OopsType";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        if (key == null) return false;
        var v = key.GetValue(ValueName) as string;
        if (string.IsNullOrWhiteSpace(v)) return false;
        return string.Equals(StripQuotes(v), GetExePath(), StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key == null) return;
        if (enabled)
            key.SetValue(ValueName, $"\"{GetExePath()}\"");
        else if (key.GetValue(ValueName) != null)
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string GetExePath() => Environment.ProcessPath ?? AppContext.BaseDirectory;
    private static string StripQuotes(string s) => s.Trim().Trim('"');
}
