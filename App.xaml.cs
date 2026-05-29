using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OopsType.Infrastructure;
using OopsType.Services;
using OopsType.Services.Localization;
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
    // Per-user single-instance gate. The "Local\" prefix scopes the mutex to the current
    // logon session so different users on the same machine can each run their own instance;
    // the GUID suffix prevents collisions with unrelated apps that happen to share a name.
    private const string SingleInstanceMutexName = @"Local\OopsType.SingleInstance.{8F2C7A14-3E5B-4E2A-9D6F-7B1C0A4F8E92}";
    private Mutex? _singleInstanceMutex;

    private IApplicationLifecycle? _lifecycle;
    private IErrorReporter? _reporter;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, createdNew: out var createdNew);
        if (!createdNew)
        {
            // Another instance already holds the mutex — release our handle without signalling
            // and bail before any UI/hooks spin up. Shutdown() here exits cleanly without
            // running OnInitialized/OnExit's lifecycle paths.
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    /// <summary>This app has no main window — every window (overlays, settings) is created on demand.</summary>
    protected override Window? CreateShell() => null;

    protected override void RegisterTypes(IContainerRegistry c)
    {
        // ---- Diagnostics (singletons — every catch block in the app calls into these) ----
        c.RegisterSingleton<ILogger, Logger>();
        c.RegisterSingleton<IErrorReporter, ErrorReporter>();
        c.RegisterSingleton<IToastService, ToastService>();

        // ---- Domain / infrastructure services (singletons — global state) ----
        c.RegisterSingleton<ITransparencyDetector, TransparencyDetector>();
        c.RegisterSingleton<ISettingsService, SettingsService>();
        c.RegisterSingleton<ILocalizationService, LocalizationService>();
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
        // Wire global exception handlers BEFORE starting services — a hook constructor or
        // settings load could itself throw, and we want those caught too.
        _reporter = Container.Resolve<IErrorReporter>();
        WireGlobalExceptionHandlers(_reporter);

        base.OnInitialized();

        try
        {
            // Apply the user's saved language BEFORE any window opens so the first SettingsWindow
            // (which can pop up on first-run) renders in the right language with the correct
            // FlowDirection. The service has already discovered packs in its constructor;
            // SetLanguage just selects which one to publish into Application.Resources.
            var loc = Container.Resolve<ILocalizationService>();
            var settings = Container.Resolve<ISettingsService>();
            loc.SetLanguage(settings.Current.General.Language);

            _lifecycle = Container.Resolve<IApplicationLifecycle>();
            _lifecycle.Start();
        }
        catch (Exception ex)
        {
            // Startup failure: log, surface, then stay alive in degraded mode so the tray icon
            // can still show (if it got that far) — the user sees the balloon and the log path.
            _reporter.Report("Startup", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _lifecycle?.Stop(); }
        catch (Exception ex) { _reporter?.Report("Shutdown", ex); }

        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { /* not owned (early shutdown) */ }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    private void WireGlobalExceptionHandlers(IErrorReporter reporter)
    {
        // UI-thread unhandled (most timer ticks, hook subscribers, settings save UI path).
        // Setting e.Handled = true keeps the app alive — the catch site can't know whether the
        // exception is recoverable, but for a tray-resident utility "stay running and log" beats
        // "die with a Watson dialog".
        DispatcherUnhandledException += (_, e) =>
        {
            reporter.Report("DispatcherUnhandledException", e.Exception);
            e.Handled = true;
        };

        // Background threads / finalizer / thread-pool throws. IsTerminating==true means CLR is
        // about to tear down — we can't prevent that, just make sure the cause is on disk.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                reporter.Report("AppDomain.UnhandledException", ex);
        };

        // Fire-and-forget Task exceptions that nobody awaited. Marking observed prevents the
        // process-killing escalation that some runtime configurations still apply.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            reporter.Report("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }
}
