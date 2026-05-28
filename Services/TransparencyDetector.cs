using Microsoft.Win32;

namespace OopsType.Services;

/// <summary>
/// Reads the OS "Transparency effects" toggle from the per-user Personalize registry key.
/// Returns <c>true</c> when the value is missing — that matches the Windows default behavior.
/// </summary>
public sealed class TransparencyDetector : ITransparencyDetector
{
    private const string PersonalizeKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "EnableTransparency";

    public bool IsTransparencyEffectsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            if (key == null) return true;
            var raw = key.GetValue(ValueName);
            // Stored as DWORD: 0 = disabled, 1 = enabled. Any other type → fall back to Windows default.
            return raw is not int i || i != 0;
        }
        catch
        {
            return true;
        }
    }
}
