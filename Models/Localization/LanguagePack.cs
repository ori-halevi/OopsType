using System.Collections.Generic;

namespace OopsType.Models.Localization;

/// <summary>
/// One language file. Deserialised from a single JSON document under <c>Languages/</c>.
/// The file is self-describing — code, names and direction live alongside the strings —
/// so adding a new language is just dropping a new JSON file. No code or resource changes.
/// </summary>
public sealed class LanguagePack
{
    /// <summary>ISO-style code used as the persisted identifier (e.g. "en", "he", "ru").</summary>
    public string Code { get; set; } = "";

    /// <summary>English/neutral display name shown when the user is choosing a language they don't yet know.</summary>
    public string Name { get; set; } = "";

    /// <summary>Display name in the language's own script (e.g. "עברית", "Русский"). Shown in the chooser.</summary>
    public string NativeName { get; set; } = "";

    /// <summary>"LeftToRight" (default) or "RightToLeft". Applied to the settings window when this language is active.</summary>
    public string FlowDirection { get; set; } = "LeftToRight";

    /// <summary>Translation table: key → translated string. Keys are the same across languages.</summary>
    public Dictionary<string, string> Strings { get; set; } = new();
}
