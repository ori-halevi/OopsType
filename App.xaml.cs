using System;
using System.Windows;
using OopsType.Services;
using OopsType.Services.Overlays;
using OopsType.ViewModels;
using OopsType.Views;
using Prism.Ioc;

namespace OopsType;

/// <summary>
/// Composition root for the WPF application. Registers DI bindings and delegates startup
/// shutdown sequencing to <see cref="IApplicationLifecycle"/> — the previous version kept
/// per-service fields and inline Start/Stop logic here, which violated SRP.
/// </summary>
public partial class App
{
    private IApplicationLifecycle? _lifecycle;

    /// <summary>This app has no main window — every window (overlays, settings) is created on demand.</summary>
    protected override Window? CreateShell() => null;

    protected override void RegisterTypes(IContainerRegistry c)
    {
        // ---- Domain / infrastructure services (singletons — global state) ----
        c.RegisterSingleton<ITransparencyDetector, TransparencyDetector>();
        c.RegisterSingleton<ISettingsService, SettingsService>();
        c.RegisterSingleton<IKeyboardLayoutService, KeyboardLayoutService>();
        c.RegisterSingleton<ICaretLocationService, CaretLocationService>();
        c.RegisterSingleton<IKeyboardActivityService, KeyboardActivityService>();
        c.RegisterSingleton<IIdleResetService, IdleResetService>();
        c.RegisterSingleton<ITaskbarService, TaskbarService>();
        c.RegisterSingleton<IStartupService, StartupService>();

        // ---- Overlay layer ----
        c.RegisterSingleton<CaretOverlayPresenter>();
        c.RegisterSingleton<MouseOverlayPresenter>();
        c.RegisterSingleton<TaskbarStripOverlayPresenter>();
        c.RegisterSingleton<IOverlayCoordinator, OverlayCoordinator>();

        // ---- ViewModels (transient — one instance per overlay/window) ----
        c.Register<CaretLabelViewModel>();
        c.Register<MouseLabelViewModel>();
        c.Register<TaskbarStripViewModel>();
        c.Register<SettingsViewModel>();

        // ---- Views (transient — overlays are recreated when toggled, settings on each open) ----
        c.Register<CaretLabelOverlay>();
        c.Register<MouseLabelOverlay>();
        c.Register<TaskbarStripOverlay>();
        c.Register<SettingsWindow>();

        // ---- Factories: let services request fresh VMs/Views without taking a Container reference. ----
        c.RegisterInstance<Func<CaretLabelViewModel>>(() => Container.Resolve<CaretLabelViewModel>());
        c.RegisterInstance<Func<MouseLabelViewModel>>(() => Container.Resolve<MouseLabelViewModel>());
        c.RegisterInstance<Func<TaskbarStripViewModel>>(() => Container.Resolve<TaskbarStripViewModel>());
        c.RegisterInstance<Func<CaretLabelOverlay>>(() => Container.Resolve<CaretLabelOverlay>());
        c.RegisterInstance<Func<MouseLabelOverlay>>(() => Container.Resolve<MouseLabelOverlay>());
        c.RegisterInstance<Func<TaskbarStripOverlay>>(() => Container.Resolve<TaskbarStripOverlay>());
        c.RegisterInstance<Func<SettingsWindow>>(() => Container.Resolve<SettingsWindow>());

        // ---- Cross-cutting view services ----
        c.RegisterSingleton<ISettingsDialog, SettingsDialog>();
        c.RegisterSingleton<ITrayPresenter, TrayPresenter>();

        // ---- Application lifecycle orchestrator ----
        c.RegisterSingleton<IApplicationLifecycle, ApplicationLifecycle>();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _lifecycle = Container.Resolve<IApplicationLifecycle>();
        _lifecycle.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _lifecycle?.Stop();
        base.OnExit(e);
    }
}
