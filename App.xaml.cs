using System;
using System.Windows;
using OopsType.Services;
using OopsType.ViewModels;
using OopsType.Views;
using Prism.Ioc;

namespace OopsType;

public partial class App
{
    private TrayIconController? _tray;
    private IKeyboardLayoutService? _layout;
    private IKeyboardActivityService? _activity;
    private IIdleResetService? _idle;
    private IOverlayCoordinator? _overlays;

    protected override Window? CreateShell() => null;

    protected override void RegisterTypes(IContainerRegistry c)
    {
        c.RegisterSingleton<ISettingsService, SettingsService>();
        c.RegisterSingleton<IKeyboardLayoutService, KeyboardLayoutService>();
        c.RegisterSingleton<ICaretLocationService, CaretLocationService>();
        c.RegisterSingleton<IKeyboardActivityService, KeyboardActivityService>();
        c.RegisterSingleton<IIdleResetService, IdleResetService>();
        c.RegisterSingleton<ITaskbarService, TaskbarService>();
        c.RegisterSingleton<IStartupService, StartupService>();
        c.RegisterSingleton<IOverlayCoordinator, OverlayCoordinator>();

        c.Register<SettingsViewModel>();
        c.Register<SettingsWindow>();

        // factory for tray to spawn settings window on demand
        c.RegisterInstance<Func<SettingsWindow>>(() => Container.Resolve<SettingsWindow>());
        c.RegisterSingleton<TrayIconController>();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        var settings = Container.Resolve<ISettingsService>();

        _layout = Container.Resolve<IKeyboardLayoutService>();
        _activity = Container.Resolve<IKeyboardActivityService>();
        _idle = Container.Resolve<IIdleResetService>();
        _overlays = Container.Resolve<IOverlayCoordinator>();
        _tray = Container.Resolve<TrayIconController>();

        _layout.Start();
        _activity.Start();
        _idle.Start();
        _overlays.Start();
        _tray.Start();

        if (settings.IsFirstLaunch)
        {
            var win = Container.Resolve<SettingsWindow>();
            win.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _overlays?.Shutdown();
        _idle?.Dispose();
        _activity?.Dispose();
        _layout?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
