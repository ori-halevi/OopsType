using Microsoft.Win32;

namespace OopsType.Services;

internal static class WindowsTransparencyDetector
{
    private const string Subkey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "EnableTransparency";

    /// <summary>
    /// Reads Settings → Personalization → Colors → Transparency effects.
    /// Defaults to true if the value is missing (matches the Windows default).
    /// </summary>
    public static bool IsTransparencyEffectsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Subkey);
            if (key == null) return true;
            var v = key.GetValue(ValueName);
            return v is not int i || i != 0;
        }
        catch
        {
            return true;
        }
    }
}
