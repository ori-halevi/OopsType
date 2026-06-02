using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
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

    // Opens the About card's links (e.g. the GitHub repo) in the user's default browser.
    // UseShellExecute is required for ShellExecute to resolve the http(s) handler; without it
    // Process.Start treats the URI as an executable path and throws. Failures are swallowed —
    // a dead "open browser" link is a cosmetic nuisance, not worth interrupting the user.
    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // Intentionally ignored — no browser, blocked URI scheme, etc.
        }
        e.Handled = true;
    }

    // ---- "Copy Language colors to the other label" -------------------------------------------
    // Lets a user mirror a palette they tuned on one label onto the other in a single click.
    // The confirm (only when the target already has rows) follows the same Wpf.Ui MessageBox
    // pattern as the unsaved-changes prompt, so the dialog styling stays consistent.
    private async void OnCopyCaretToMouse(object sender, RoutedEventArgs e) => await CopyLabelColorsAsync(fromCaretToMouse: true);

    private async void OnCopyMouseToCaret(object sender, RoutedEventArgs e) => await CopyLabelColorsAsync(fromCaretToMouse: false);

    private async Task CopyLabelColorsAsync(bool fromCaretToMouse)
    {
        var targetHasColors = fromCaretToMouse ? _vm.MouseHasColors : _vm.CaretHasColors;
        if (targetHasColors)
        {
            var dialog = new Wpf.Ui.Controls.MessageBox
            {
                Title = _loc.T("Dialog_UnsavedTitle"),
                Content = _loc.T("Label_CopyConfirmMessage"),
                PrimaryButtonText = _loc.T("Label_CopyConfirmReplace"),
                CloseButtonText = _loc.T("Dialog_Cancel"),
                PrimaryButtonAppearance = ControlAppearance.Primary,
            };
            if (await dialog.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        }

        _vm.CopyLabelColors(fromCaretToMouse);
    }

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
