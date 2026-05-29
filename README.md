# OopsType

**Stop typing in the wrong language.**

OopsType is a lightweight Windows tray utility that gives you an unmissable, glanceable indication of your active keyboard layout — exactly where your eyes already are. No more typing a full sentence before realizing you were in the wrong layout the whole time.

Built with WPF on .NET 9 (Windows) and the [WPF-UI](https://github.com/lepoco/wpfui) Fluent design system.

---

## Why this exists

Windows shows the current keyboard layout in a tiny tray badge that nobody actually looks at while typing. If you switch between Hebrew/English, Russian/English, Greek/English (or any pair) dozens of times a day, you've felt the pain: you focus a textbox, start typing, glance up — and realize the last paragraph is gibberish.

OopsType solves this by putting the layout indicator **where your attention already is**: next to your caret, next to your mouse, and along the edge of your taskbar as a colored strip you can see from across the room.

## Features

OopsType ships four independent overlays you can mix and match — turn on only what you want.

### 1. Caret label
A tiny floating chip that hovers above your text caret in whatever app currently has focus, showing the active layout (e.g. `EN`, `עב`, `РУ`). It hides automatically over menus, tooltips and other places a caret would be a lie. Uses Windows `GUITHREADINFO` first, then falls back to UI Automation's `TextPattern` for apps that don't expose a Win32 caret.

### 2. Mouse label
A small chip that follows your mouse cursor. Useful in apps without a visible text caret (web pages, IDEs with custom carets, etc.). Two tracking modes:
- **Economy** *(default)*: zero work while the cursor is at rest. A low-level mouse hook wakes the render loop on motion; it goes back to idle ~150 ms after the cursor stops. Recommended for laptops.
- **Max-smoothness**: keeps the per-frame compositor subscription alive permanently. Marginally snappier first-frame response, at the cost of constant background activity.

Also respects cursor-hide events (video players, touch input) — when Windows hides the cursor, the label hides with it.

### 3. Taskbar color strip
A colored bar painted over (or behind) your taskbar in the color you assigned to the active layout. Glanceable from across the screen, even in your peripheral vision. Fully configurable:
- Thickness: small (3 px) / medium / large / **full** (entire taskbar height)
- Vertical anchor: top or bottom of the taskbar
- Z-order: in front of taskbar icons, or behind them (lets the Windows 11 acrylic blur the color subtly into the bar)
- Opacity: independently toggleable

### 4. Idle reset
Optional auto-revert: after N seconds without keypresses, switch the focused window's layout to a target language (e.g. always back to English when you walk away). One-shot per idle stretch — it won't fight you while you continue to idle. Mouse movement doesn't reset the timer; only real keystrokes do.

---

## Installing

### Requirements
- Windows 10 / 11
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) (or build self-contained, see below)

### From source
```powershell
git clone https://github.com/ori-halevi/OopsType.git
cd OopsType
dotnet build -c Release
```

To produce a portable single-file `.exe`:
```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The output ends up in `bin\Release\net9.0-windows\win-x64\publish\OopsType.exe`. Copy it anywhere; it has no installer.

### Autostart with Windows
Enable **General → Launch on Windows startup** in the settings window. OopsType writes an entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` — no admin rights needed, no installer state.

Settings are stored at `%LOCALAPPDATA%\OopsType\settings.json`.

---

## Usage

Launch `OopsType.exe`. A small tray icon appears in the notification area.

| Action | How |
|---|---|
| Open settings | Double-click the tray icon, or right-click → **Settings…** |
| Quickly toggle an overlay | Right-click tray → check/uncheck **Caret label / Mouse label / Taskbar strip / Idle reset** |
| Quit | Right-click tray → **Quit** |

The settings window has a live preview for every overlay, so you can dial in offsets, fonts and colors and see the result immediately without saving.

---

## Architecture

OopsType is organized as a small, dependency-injected WPF app (Prism.Unity container). Each concern lives in its own class so behavior can be reasoned about — and toggled — in isolation.

```
App.xaml.cs                      ── composition root (DI registration)
└── ApplicationLifecycle         ── orchestrates start/stop in order

Services/
├── KeyboardLayoutService        ── tracks foreground HKL via WinEvent hook + 80 ms poll
├── KeyboardActivityService      ── low-level keyboard hook → KeyPressed event + LastKeyTimeUtc
├── CaretLocationService         ── GUITHREADINFO → UI Automation TextPattern fallback
├── TaskbarService               ── locates the primary Shell_TrayWnd rect
├── IdleResetService             ── 1 Hz tick; reverts layout when idle > threshold
├── SettingsService              ── JSON persistence (tmp-then-rename), Changed event
├── StartupService               ── HKCU\…\Run autostart toggle
├── TransparencyDetector         ── reads Win11 acrylic preference for first-run defaults
├── LocaleResolver               ── HKL → native-script label (en→"EN", he→"עב", ja→"日本")
└── Overlays/
    ├── CaretOverlayPresenter         ── owns + positions caret chip; 120 ms follow loop
    ├── MouseOverlayPresenter         ── hook-wakes-Rendering pattern (see file header)
    └── TaskbarStripOverlayPresenter  ── repositions on heartbeat when the taskbar moves

Native/
├── LowLevelKeyboardHook        ── WH_KEYBOARD_LL
├── LowLevelMouseHook           ── WH_MOUSE_LL
├── WinEventHook                ── SetWinEventHook for foreground/focus
└── NativeMethods               ── all P/Invoke signatures

Views/                           ── per-overlay WPF windows + the Fluent settings window
ViewModels/                      ── settings + per-overlay VMs (INotifyPropertyChanged)
Infrastructure/                  ── ILogger, IErrorReporter, IToastService, Safe.Invoke
```

### Design notes
- **No main window.** Every window — overlays and the settings dialog — is created on demand. `App.CreateShell()` returns `null` and `ShutdownMode="OnExplicitShutdown"` keeps the process alive in the tray.
- **One shared heartbeat.** `OverlayCoordinator` runs a single 1.5 s `DispatcherTimer` and ticks all three presenters, instead of each owning its own. Keeps idle CPU near zero.
- **Hook callbacks never throw.** Every low-level hook delegate is wrapped in `Safe.Invoke` so a buggy subscriber can't degrade the global hook chain (Windows enforces `LowLevelHooksTimeout` — a slow callback gets you uninstalled).
- **Mouse overlay: hook-wakes-Rendering.** The cursor-follow chip used to run on a 40 Hz `DispatcherTimer`, which produced visible jitter because it wasn't synchronized to the WPF compositor. Now a low-level mouse hook just marks "moved" and subscribes the window to `CompositionTarget.Rendering`; window moves happen once per frame, frame-synchronous. After 150 ms idle the Rendering subscription is dropped — true zero-work idle.
- **Crash-resilient settings I/O.** `SettingsService.WriteToDisk` writes to `settings.json.tmp` then atomically `File.Move(..., overwrite: true)` so a crash mid-write can't leave a truncated config.
- **Global exception handlers.** `App.WireGlobalExceptionHandlers` catches `DispatcherUnhandledException`, `AppDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException`. Errors are logged AND surfaced as a tray toast — silent failures in a long-running utility are the worst kind.

---

## Tech stack

- **.NET 9** (`net9.0-windows`), WPF + WinForms interop (the tray icon uses `System.Windows.Forms.NotifyIcon`)
- **[Prism.Unity](https://github.com/PrismLibrary/Prism) 9.0** — DI container and `PrismApplication` host
- **[WPF-UI](https://github.com/lepoco/wpfui) 4.0** — Fluent / Mica visuals for the settings window
- **Win32 P/Invoke** — `user32`, `kernel32`, `advapi32` for hooks, foreground tracking, layout switching, and the Run registry key
- **UI Automation** — fallback caret detection in apps without a classical Win32 caret

No telemetry. No network calls. No third-party services. Everything runs locally.

---

## Contributing

Issues and PRs are welcome at [github.com/ori-halevi/OopsType](https://github.com/ori-halevi/OopsType). A few notes:

- The codebase favors small, single-responsibility classes coordinated through interfaces — please keep that style when adding features.
- Anything running on a hook callback thread (`LowLevelKeyboardHook`, `LowLevelMouseHook`, `WinEventHook`) **must** be wrapped in `Safe.Invoke` and must do the minimum amount of work possible.
- New settings go into `Models/AppSettings.cs`; per-feature defaults belong in `SettingsService.ApplyFirstRunDefaults` if they should adapt to system state.

---

## License

Apache License 2.0 — see [LICENSE](LICENSE).
