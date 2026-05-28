using System;

namespace OopsType.Services.Overlays;

/// <summary>
/// Single overlay's lifecycle from the coordinator's point of view. Each implementation owns one
/// overlay <c>Window</c> + its <c>ViewModel</c> and decides whether it should currently be visible
/// based on settings. Splitting these per overlay keeps each class focused (SRP) and avoids the
/// "god coordinator" that previously juggled three overlays at once.
/// </summary>
public interface IOverlayPresenter : IDisposable
{
    /// <summary>Re-read settings and create/destroy the overlay window accordingly.</summary>
    void ApplySettings();

    /// <summary>
    /// Periodic tick from the coordinator's heartbeat — used to re-assert window position/z-order
    /// without each presenter owning its own timer.
    /// </summary>
    void Heartbeat();
}
