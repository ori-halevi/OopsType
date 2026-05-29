namespace OopsType.Services;

public interface IOverlayCoordinator
{
    void Start();
    void ApplySettings();
    void Shutdown();

    /// <summary>
    /// Hint to the coordinator that the OS just resumed from sleep/hibernate. Lets the next
    /// heartbeat short-circuit its watchdog gate and re-arm hooks immediately, instead of
    /// waiting up to 30s while a confused user wonders why their keyboard layout isn't
    /// reacting.
    /// </summary>
    void OnSystemResume();
}
