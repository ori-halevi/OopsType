using System;

namespace OopsType.Services;

/// <summary>
/// System-tray "view": shows the OopsType icon, exposes a context menu, and forwards
/// user actions to the underlying settings/dialog services.
/// </summary>
public interface ITrayPresenter : IDisposable
{
    /// <summary>Create the tray icon and wire up its menu. Idempotent — safe to call once.</summary>
    void Start();
}
