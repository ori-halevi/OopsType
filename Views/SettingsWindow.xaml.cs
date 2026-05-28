using System.ComponentModel;
using System.Windows;
using OopsType.ViewModels;

namespace OopsType.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_vm.IsDirty) return;

        var result = MessageBox.Show(
            this,
            "You have unsaved changes. Save them before closing?",
            "OopsType",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        switch (result)
        {
            case MessageBoxResult.Yes:
                _vm.Apply();
                break;
            case MessageBoxResult.Cancel:
                e.Cancel = true;
                break;
            // No: just close, changes discarded (settings.json keeps last saved state)
        }
    }
}
