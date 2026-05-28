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
}
