using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using OopsType.Services.Localization;
using OopsType.ViewModels;
using Wpf.Ui.Controls;

namespace OopsType.Views;

public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly SettingsViewModel _vm;
    private readonly ILocalizationService _loc;
    private bool _confirmedClose;

    public SettingsWindow(SettingsViewModel vm, ILocalizationService loc)
    {
        InitializeComponent();
        _vm = vm;
        _loc = loc;
        DataContext = vm;
        Closing += OnClosing;

        // FlowDirection isn't a DynamicResource — it's a per-window enum the OS uses to flip
        // child layout. We set it explicitly at construction from the active language pack,
        // and re-apply it whenever the language changes mid-session (Save in the General tab
        // calls SetLanguage, which raises LanguageChanged).
        ApplyFlowDirection();
        _loc.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => _loc.LanguageChanged -= OnLanguageChanged;

        // Editable ComboBox / NumberBox bindings sometimes push back during binding initialization
        // (e.g. when ItemsSource loads and the editable text bounces through empty), which marks
        // the VM dirty before the user has touched anything. Clear that initial noise once the
        // dispatcher goes idle — anything the user does after that is a real change.
        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(_vm.ResetDirty), DispatcherPriority.ApplicationIdle);
    }

    private void OnLanguageChanged() =>
        Dispatcher.BeginInvoke(new Action(ApplyFlowDirection));

    private void ApplyFlowDirection()
    {
        FlowDirection = string.Equals(_loc.CurrentLanguage.FlowDirection, "RightToLeft", StringComparison.OrdinalIgnoreCase)
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
    }

    // async void is intentional: WPF Closing is sync, so we cancel it, await the dialog, and
    // re-issue Close() once the current Closing event has fully unwound.
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_confirmedClose || !_vm.IsDirty) return;

        e.Cancel = true;

        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = _loc.T("Dialog_UnsavedTitle"),
            Content = _loc.T("Dialog_UnsavedMessage"),
            PrimaryButtonText = _loc.T("Dialog_Save"),
            SecondaryButtonText = _loc.T("Dialog_Discard"),
            CloseButtonText = _loc.T("Dialog_Cancel"),
            PrimaryButtonAppearance = ControlAppearance.Primary,
        };

        var result = await dialog.ShowDialogAsync();

        switch (result)
        {
            case Wpf.Ui.Controls.MessageBoxResult.Primary: // Save
                // If persistence fails (disk full, locked file), don't close on top of an unsaved
                // VM — that would silently lose the user's edits. Keep the window open and let
                // them retry or discard explicitly.
                try { _vm.Apply(); }
                catch
                {
                    // _vm.Apply already routes failures through IErrorReporter; the toast will
                    // appear. Stay open so the user can react instead of seeing their changes
                    // vanish.
                    return;
                }
                if (_vm.IsDirty) return;
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
