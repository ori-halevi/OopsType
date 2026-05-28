using System;

namespace OopsType.Infrastructure;

/// <summary>
/// Helpers for invoking a callback that runs on a thread we can't let crash
/// (DispatcherTimer ticks, CompositionTarget.Rendering, hook callbacks). Any throw is
/// routed to <see cref="IErrorReporter"/> with the caller-supplied <paramref name="source"/>
/// label so the log/balloon names the offending site (not just "unhandled").
/// </summary>
internal static class Safe
{
    public static void Invoke(IErrorReporter reporter, string source, Action action)
    {
        try { action(); }
        catch (Exception ex) { reporter.Report(source, ex); }
    }
}
