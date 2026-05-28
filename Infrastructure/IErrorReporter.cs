using System;

namespace OopsType.Infrastructure;

/// <summary>
/// Central error sink. Every catch block in the app calls <see cref="Report(string, Exception)"/>
/// instead of swallowing silently — the report lands in the log, and per-source throttling
/// decides whether to also raise <see cref="Notified"/> for UI surfacing (tray balloon).
/// </summary>
public interface IErrorReporter
{
    /// <summary>Logs the exception and (subject to throttling) fires <see cref="Notified"/>.</summary>
    void Report(string source, Exception ex);

    /// <summary>Logs a warning string (no exception) and (subject to throttling) fires <see cref="Notified"/>.</summary>
    void Report(string source, string message);

    /// <summary>
    /// Raised after a report passes throttling. Subscribers MUST tolerate being called from
    /// any thread (hook callbacks, background tasks) and marshal to the UI thread themselves
    /// before touching UI state.
    /// </summary>
    event Action<ErrorNotification>? Notified;
}

public readonly record struct ErrorNotification(string Source, string Message);
