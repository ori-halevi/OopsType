namespace OopsType.Services;

/// <summary>
/// Abstraction over the OS "Transparency effects" preference (Settings → Personalization → Colors).
/// Extracted so consumers can be unit-tested without touching the registry, and so the implementation
/// detail (registry on Windows) is not hardwired into business logic.
/// </summary>
public interface ITransparencyDetector
{
    /// <summary>True when Windows transparency effects are enabled (the default).</summary>
    bool IsTransparencyEffectsEnabled();
}
