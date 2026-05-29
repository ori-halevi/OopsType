using System;
using System.Collections.Generic;
using System.Windows;
using OopsType.Models.Localization;

namespace OopsType.Services.Localization;

/// <summary>
/// Runtime language switcher. Discovers language packs at startup, exposes the list to the
/// settings UI, and republishes the active translations into <see cref="Application.Resources"/>
/// so any <c>{DynamicResource}</c> or <c>{loc:T}</c> binding refreshes automatically.
/// </summary>
public interface ILocalizationService
{
    /// <summary>All language packs discovered on disk, ordered by display name.</summary>
    IReadOnlyList<LanguagePack> AvailableLanguages { get; }

    /// <summary>The currently active language pack. Never null after construction.</summary>
    LanguagePack CurrentLanguage { get; }

    /// <summary>Raised after <see cref="SetLanguage"/> swaps the active dictionary. WinForms-side
    /// consumers (e.g. the tray menu) subscribe to this to rebuild their text — they can't bind
    /// to WPF DynamicResource.</summary>
    event Action? LanguageChanged;

    /// <summary>
    /// Switch to the language identified by <paramref name="code"/>. Falls back to the default
    /// pack (or the first available) when the code is unknown. Persisting the choice is the
    /// caller's responsibility — this method only updates in-memory state and resources.
    /// </summary>
    void SetLanguage(string code);

    /// <summary>Translate a single key. Used by code that can't reach into WPF resources
    /// (the tray menu, code-behind dialogs). Returns the key itself when no translation exists,
    /// so a missing entry is obvious in the UI rather than blank.</summary>
    string T(string key);
}
