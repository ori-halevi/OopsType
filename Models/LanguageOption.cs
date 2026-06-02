namespace OopsType.Models;

/// <summary>
/// One selectable language in the "add language" picker. <see cref="IsInstalled"/> marks layouts
/// actually present on this machine so the picker can surface and badge them. <see cref="Group"/>
/// is the (already-localized) section header used to group the list.
/// </summary>
public sealed record LanguageOption(
    string Code,
    string NativeName,
    string EnglishName,
    bool IsInstalled,
    string Group);
