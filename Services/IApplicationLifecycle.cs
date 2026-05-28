namespace OopsType.Services;

/// <summary>
/// Orchestrates application startup and shutdown ordering. Lets <c>App.xaml.cs</c> remain a
/// thin composition root with no service-specific knowledge.
/// </summary>
public interface IApplicationLifecycle
{
    /// <summary>Start every background service and open the first-launch settings dialog if needed.</summary>
    void Start();

    /// <summary>Tear down services in reverse-dependency order.</summary>
    void Stop();
}
