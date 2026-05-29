using System;
using System.ComponentModel;
using System.Windows.Threading;
using OopsType.ViewModels;
using Wpf.Ui.Controls;

namespace OopsType.Views;

public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly SettingsViewModel _vm;
    private bool _confirmedClose;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Closing += OnClosing;

        // Editable ComboBox / NumberBox bindings sometimes push back during binding initialization
        // (e.g. when ItemsSource loads and the editable text bounces through empty), which marks
        // the VM dirty before the user has touched anything. Clear that initial noise once the
        // dispatcher goes idle — anything the user does after that is a real change.
        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(_vm.ResetDirty), DispatcherPriority.ApplicationIdle);
    }

    // async void is intentional: WPF Closing is sync, so we cancel it, await the dialog, and
    // re-issue Close() once the current Closing event has fully unwound.
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_confirmedClose || !_vm.IsDirty) return;

        e.Cancel = true;

        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = "OopsType",
            Content = "You have unsaved changes. Save them before closing?",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Discard",
            CloseButtonText = "Cancel",
            PrimaryButtonAppearance = ControlAppearance.Primary,
        };

        var result = await dialog.ShowDialogAsync();

        switch (result)
        {
            case Wpf.Ui.Controls.MessageBoxResult.Primary: // Save
                _vm.Apply();
                break;
            case Wpf.Ui.Controls.MessageBoxResult.Secondary: // Discard
                break;
            default: // None — user dismissed (Cancel / Esc / X). Stay open.
                return;
        }

        _confirmedClose = true;
        // Defer Close so we're out of the original Closing handler frame — calling Close() while
        // the window is still in its "closing" state throws InvalidOperationException.
        Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Background);
    }
}
