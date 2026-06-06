namespace OopsType.Services;

/// <summary>
/// Shows or hides the built-in Windows language/input indicator (the legacy "language bar"
/// exposed through the Text Services Framework). Lets the user lean on OopsType's own overlays
/// and reclaim the taskbar space the ENG/HEB switcher occupies. Opt-in; default off.
/// </summary>
public interface ILanguageBarService
{
    /// <summary>True if the Windows language indicator is currently hidden.</summary>
    bool IsHidden();

    /// <summary>Hide (<paramref name="hidden"/> = true) or restore the Windows language indicator.</summary>
    void SetHidden(bool hidden);
}
