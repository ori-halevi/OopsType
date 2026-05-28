using System;
using OopsType.Models;

namespace OopsType.Services;

public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>True if no settings file existed at startup (defaults were persisted just now).</summary>
    bool IsFirstLaunch { get; }

    event Action? Changed;
    void Save();
    void Reload();
}
