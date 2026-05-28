using System;
using System.Windows;
using OopsType.Views;

namespace OopsType.Services;

/// <summary>
/// Default <see cref="ISettingsDialog"/> implementation that resolves a fresh
/// <see cref="SettingsWindow"/> per request via DI, but reuses any already-visible instance.
/// </summary>
public sealed class SettingsDialog : ISettingsDialog
{
    private readonly Func<SettingsWindow> _windowFactory;

    public SettingsDialog(Func<SettingsWindow> windowFactory) => _windowFactory = windowFactory;

    public void Show()
    {
        // Single-instance behavior: re-activate an existing window rather than spawning a duplicate.
        foreach (Window w in Application.Current.Windows)
        {
            if (w is SettingsWindow existing)
            {
                existing.Activate();
                return;
            }
        }

        var window = _windowFactory();
        window.Show();
        window.Activate();
    }
}
