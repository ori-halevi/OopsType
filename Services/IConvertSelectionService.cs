using System;

namespace OopsType.Services;

/// <summary>
/// Watches for keyboard-layout switches and, when the user had text selected at that moment,
/// re-types that text as though it had been typed on the layout they meant to use.
/// </summary>
public interface IConvertSelectionService : IDisposable
{
    /// <summary>Subscribe to layout changes and spin up the conversion worker. Call from the UI thread.</summary>
    void Start();
}
