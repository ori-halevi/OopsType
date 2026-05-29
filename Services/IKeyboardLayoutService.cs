using System;
using System.Collections.Generic;
using OopsType.Models;

namespace OopsType.Services;

public interface IKeyboardLayoutService : IDisposable
{
    LanguageInfo Current { get; }
    event Action<LanguageInfo>? LanguageChanged;
    IReadOnlyList<LanguageInfo> GetInstalledLayouts();
    bool RequestLanguage(string twoLetterCode);
    void Start();

    /// <summary>
    /// Reinstalls the focus-change WinEvent hook if it isn't currently active, and forces an
    /// immediate layout re-check (covers resume-from-sleep and silent unhook by Windows). Safe
    /// from a watchdog tick; never throws.
    /// </summary>
    void EnsureInstalled();
}
