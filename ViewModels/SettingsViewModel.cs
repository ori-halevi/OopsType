using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OopsType.Infrastructure;
using OopsType.Models;
using OopsType.Models.Localization;
using OopsType.Services;
using OopsType.Services.Localization;
using Prism.Commands;
using Prism.Mvvm;

namespace OopsType.ViewModels;

public sealed class SettingsViewModel : BindableBase
{
    private readonly ISettingsService _settings;
    private readonly IKeyboardLayoutService _layout;
    private readonly IStartupService _startup;
    private readonly ILocalizationService _localization;
    private readonly IErrorReporter _reporter;
    private readonly bool _initialized;

    public SettingsViewModel(ISettingsService settings, IKeyboardLayoutService layout, IStartupService startup, ILocalizationService localization, IErrorReporter reporter)
    {
        _settings = settings;
        _layout = layout;
        _startup = startup;
        _localization = localization;
        _reporter = reporter;

        AvailableLanguages = new ObservableCollection<LanguagePack>(_localization.AvailableLanguages);

        // The row collections are created once and only ever repopulated in-place (LoadFromSettings),
        // so the CollectionChanged subscriptions wired below survive a Discard.
        CaretColorRows = new ObservableCollection<LabelStyleRow>();
        MouseColorRows = new ObservableCollection<LabelStyleRow>();
        ColorRows = new ObservableCollection<LangColorRow>();

        // Seed every editable field straight from the persisted settings. Reused verbatim by
        // Discard() to throw away unsaved edits.
        LoadFromSettings();

        LanguageOptions = BuildLanguageOptions();

        SaveCommand = new DelegateCommand(Apply, () => IsDirty).ObservesProperty(() => IsDirty);
        DiscardCommand = new DelegateCommand(Discard, () => IsDirty).ObservesProperty(() => IsDirty);
        AddColorCommand = new DelegateCommand<string>(AddStripColor);
        RemoveColorCommand = new DelegateCommand<LangColorRow>(RemoveColorRow);
        AddCaretColorCommand = new DelegateCommand<string>(code => AddLabelRow(CaretColorRows, code));
        RemoveCaretColorCommand = new DelegateCommand<LabelStyleRow>(r => { if (r != null) CaretColorRows.Remove(r); });
        AddMouseColorCommand = new DelegateCommand<string>(code => AddLabelRow(MouseColorRows, code));
        RemoveMouseColorCommand = new DelegateCommand<LabelStyleRow>(r => { if (r != null) MouseColorRows.Remove(r); });

        // ---- dirty tracking ----
        // Subscribe AFTER all backing fields are seeded so initial assignments don't mark dirty.
        ColorRows.CollectionChanged += OnColorRowsChanged;
        foreach (var row in ColorRows) row.PropertyChanged += OnRowChanged;

        CaretColorRows.CollectionChanged += OnCaretRowsChanged;
        foreach (var row in CaretColorRows) row.PropertyChanged += OnCaretRowChanged;
        MouseColorRows.CollectionChanged += OnMouseRowsChanged;
        foreach (var row in MouseColorRows) row.PropertyChanged += OnMouseRowChanged;

        PropertyChanged += OnVmPropertyChanged;
        _initialized = true;
    }

    // System-installed font families — computed once per VM instance (cheap, but the list never changes mid-session).
    public IReadOnlyList<string> AvailableFonts { get; } = Fonts.SystemFontFamilies
        .Select(f => f.Source)
        .Distinct()
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    // Clamp window for label offsets. Even on a 16K wide multi-monitor setup the largest
    // sensible offset is a few thousand pixels — anything past this is almost certainly a
    // fat-fingered value (or a malformed settings.json) and would push the label off every
    // screen, making the overlay look broken with no obvious cause. ±10_000 also keeps
    // `cursor + offset` away from int overflow even if the cursor itself is at int.MaxValue,
    // which a misbehaving virtual-display driver can theoretically report.
    private const int MaxOffsetPx = 10_000;
    private const int MinFontSize = 6;
    private const int MaxFontSize = 96;

    // ---- Caret label (follows text caret) ----
    private bool _caretEnabled;
    public bool CaretEnabled { get => _caretEnabled; set => SetProperty(ref _caretEnabled, value); }

    private int _caretOffsetX;
    public int CaretOffsetX { get => _caretOffsetX; set => SetProperty(ref _caretOffsetX, ClampOffset(value)); }
    private int _caretOffsetY;
    public int CaretOffsetY { get => _caretOffsetY; set => SetProperty(ref _caretOffsetY, ClampOffset(value)); }
    private string _caretFont;
    public string CaretFont { get => _caretFont; set => SetProperty(ref _caretFont, value); }
    private int _caretSize;
    public int CaretSize { get => _caretSize; set => SetProperty(ref _caretSize, ClampFontSize(value)); }
    private double _caretOpacity;
    public double CaretOpacity
    {
        get => _caretOpacity;
        set { if (SetProperty(ref _caretOpacity, Math.Clamp(value, 0.0, 1.0))) RaisePropertyChanged(nameof(CaretPreviewOpacity)); }
    }

    // ---- Mouse label (follows mouse cursor) ----
    private bool _mouseEnabled;
    public bool MouseEnabled { get => _mouseEnabled; set => SetProperty(ref _mouseEnabled, value); }
    private int _mouseOffsetX;
    public int MouseOffsetX { get => _mouseOffsetX; set => SetProperty(ref _mouseOffsetX, ClampOffset(value)); }
    private int _mouseOffsetY;
    public int MouseOffsetY { get => _mouseOffsetY; set => SetProperty(ref _mouseOffsetY, ClampOffset(value)); }
    private string _mouseFont;
    public string MouseFont { get => _mouseFont; set => SetProperty(ref _mouseFont, value); }
    private int _mouseSize;
    public int MouseSize { get => _mouseSize; set => SetProperty(ref _mouseSize, ClampFontSize(value)); }
    private double _mouseOpacity;
    public double MouseOpacity
    {
        get => _mouseOpacity;
        set { if (SetProperty(ref _mouseOpacity, Math.Clamp(value, 0.0, 1.0))) RaisePropertyChanged(nameof(MousePreviewOpacity)); }
    }

    private static int ClampOffset(int value) => Math.Clamp(value, -MaxOffsetPx, MaxOffsetPx);
    private static int ClampFontSize(int value) => value <= 0 ? value : Math.Clamp(value, MinFontSize, MaxFontSize);

    public string[] MouseTrackingModes { get; } = new[] { "economy", "max-smoothness" };
    private string _mouseTrackingMode;
    public string MouseTrackingMode { get => _mouseTrackingMode; set => SetProperty(ref _mouseTrackingMode, value); }

    private static string NormalizeTrackingMode(string? mode) =>
        string.Equals(mode, "max-smoothness", StringComparison.OrdinalIgnoreCase) ? "max-smoothness" : "economy";

    // ---- Caret / Mouse per-language color tables ----
    // Each label overlay gets its own table (the user asked for them to be independent), so
    // editing the caret palette never touches the mouse palette and vice-versa.
    public ObservableCollection<LabelStyleRow> CaretColorRows { get; }
    public ObservableCollection<LabelStyleRow> MouseColorRows { get; }

    /// <summary>True when the caret/mouse table has at least one language row. Gates the
    /// "copy to the other label" button so the user can't mirror an empty palette.</summary>
    public bool CaretHasColors => CaretColorRows.Count > 0;
    public bool MouseHasColors => MouseColorRows.Count > 0;

    /// <summary>
    /// Replaces one label's per-language colors with a clone of the other's, so a user who has
    /// carefully tuned (say) the caret palette can mirror it onto the mouse label in one click.
    /// Rows are deep-copied (not shared) so the two tables stay independently editable afterwards.
    /// </summary>
    public void CopyLabelColors(bool fromCaretToMouse)
    {
        var source = fromCaretToMouse ? CaretColorRows : MouseColorRows;
        var target = fromCaretToMouse ? MouseColorRows : CaretColorRows;
        target.Clear();
        foreach (var r in source)
            target.Add(new LabelStyleRow
            {
                Code = r.Code,
                Background = r.Background,
                Foreground = r.Foreground,
                BorderColor = r.BorderColor,
                BorderThickness = r.BorderThickness,
            });
    }

    private static void ReloadLabelRows(ObservableCollection<LabelStyleRow> rows, Dictionary<string, LabelLangStyle> source)
    {
        rows.Clear();
        foreach (var kv in source)
            rows.Add(new LabelStyleRow
            {
                Code = kv.Key,
                Background = kv.Value.Background,
                Foreground = kv.Value.Foreground,
                BorderColor = kv.Value.BorderColor,
                BorderThickness = kv.Value.BorderThickness,
            });
    }

    private static void ApplyLabelRows(ObservableCollection<LabelStyleRow> rows, Dictionary<string, LabelLangStyle> target)
    {
        target.Clear();
        foreach (var row in rows)
        {
            var code = (row.Code ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(code)) continue;
            target[code] = new LabelLangStyle
            {
                Background = string.IsNullOrWhiteSpace(row.Background) ? "#CC222222" : row.Background.Trim(),
                Foreground = string.IsNullOrWhiteSpace(row.Foreground) ? "#FFFFFFFF" : row.Foreground.Trim(),
                BorderColor = string.IsNullOrWhiteSpace(row.BorderColor) ? "#FF000000" : row.BorderColor.Trim(),
                BorderThickness = Math.Clamp(row.BorderThickness, 0, 20),
            };
        }
    }

    // New rows start from the built-in dark-chip look so a freshly-added language is immediately
    // visible (rather than a blank/transparent chip the user then has to figure out).
    private static LabelStyleRow NewLabelRow() => new()
    {
        Code = "",
        Background = "#CC222222",
        Foreground = "#FFFFFFFF",
        BorderColor = "#FF000000",
        BorderThickness = 0,
    };

    // Add a language row from the picker. Silently ignores blanks and duplicates (the same code
    // twice would just shadow itself), so the picker never produces a broken table.
    private static void AddLabelRow(ObservableCollection<LabelStyleRow> rows, string? code)
    {
        var c = (code ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(c)) return;
        foreach (var r in rows)
            if (string.Equals((r.Code ?? "").Trim(), c, StringComparison.OrdinalIgnoreCase)) return;
        var row = NewLabelRow();
        row.Code = c;
        rows.Add(row);
    }

    // ---- Caret / Mouse live-preview projections ----
    // The preview chip uses the FIRST configured row (mirrors the strip preview's "first row wins"
    // rule), falling back to the built-in dark chip + "EN" when no usable row exists yet.
    private static LabelStyleRow? FirstUsableRow(ObservableCollection<LabelStyleRow> rows)
    {
        foreach (var r in rows)
            if (!string.IsNullOrWhiteSpace(r.Code)) return r;
        return null;
    }

    private static string PreviewCodeOf(ObservableCollection<LabelStyleRow> rows)
        => FirstUsableRow(rows)?.Code is { Length: > 0 } code ? code.Trim().ToUpperInvariant() : "EN";

    private static Brush PreviewBrushOf(ObservableCollection<LabelStyleRow> rows, Func<LabelStyleRow, string> pick, Brush fallback)
    {
        var row = FirstUsableRow(rows);
        if (row == null) return fallback;
        return LabelStyleBrushes.ParseQuiet(pick(row)) ?? fallback;
    }

    private static readonly Brush PreviewDefaultBackground = LabelStyleBrushes.ParseQuiet("#CC222222")!;
    private static readonly Brush PreviewDefaultForeground = LabelStyleBrushes.ParseQuiet("#FFFFFFFF")!;
    private static readonly Brush PreviewTransparent = LabelStyleBrushes.ParseQuiet("#00000000")!;

    public string CaretPreviewCode => PreviewCodeOf(CaretColorRows);
    public Brush CaretPreviewBackground => PreviewBrushOf(CaretColorRows, r => r.Background, PreviewDefaultBackground);
    public Brush CaretPreviewForeground => PreviewBrushOf(CaretColorRows, r => r.Foreground, PreviewDefaultForeground);
    public Brush CaretPreviewBorderBrush => PreviewBorderBrushOf(CaretColorRows);
    public Thickness CaretPreviewBorderThickness => PreviewBorderThicknessOf(CaretColorRows);
    public double CaretPreviewOpacity => _caretOpacity;

    public string MousePreviewCode => PreviewCodeOf(MouseColorRows);
    public Brush MousePreviewBackground => PreviewBrushOf(MouseColorRows, r => r.Background, PreviewDefaultBackground);
    public Brush MousePreviewForeground => PreviewBrushOf(MouseColorRows, r => r.Foreground, PreviewDefaultForeground);
    public Brush MousePreviewBorderBrush => PreviewBorderBrushOf(MouseColorRows);
    public Thickness MousePreviewBorderThickness => PreviewBorderThicknessOf(MouseColorRows);
    public double MousePreviewOpacity => _mouseOpacity;

    private static Brush PreviewBorderBrushOf(ObservableCollection<LabelStyleRow> rows)
    {
        var row = FirstUsableRow(rows);
        if (row == null || row.BorderThickness <= 0) return PreviewTransparent;
        return LabelStyleBrushes.ParseQuiet(row.BorderColor) ?? PreviewTransparent;
    }

    private static Thickness PreviewBorderThicknessOf(ObservableCollection<LabelStyleRow> rows)
    {
        var row = FirstUsableRow(rows);
        if (row == null || row.BorderThickness <= 0) return new Thickness(0);
        // Only show the border in the preview if the color actually parses, so a bad hex doesn't
        // leave a phantom border with no visible stroke.
        return LabelStyleBrushes.ParseQuiet(row.BorderColor) == null ? new Thickness(0) : new Thickness(row.BorderThickness);
    }

    private static readonly string[] CaretPreviewProps =
    {
        nameof(CaretPreviewCode), nameof(CaretPreviewBackground), nameof(CaretPreviewForeground),
        nameof(CaretPreviewBorderBrush), nameof(CaretPreviewBorderThickness),
    };
    private static readonly string[] MousePreviewProps =
    {
        nameof(MousePreviewCode), nameof(MousePreviewBackground), nameof(MousePreviewForeground),
        nameof(MousePreviewBorderBrush), nameof(MousePreviewBorderThickness),
    };

    private void RaiseCaretPreview() { foreach (var p in CaretPreviewProps) RaisePropertyChanged(p); }
    private void RaiseMousePreview() { foreach (var p in MousePreviewProps) RaisePropertyChanged(p); }

    private void OnCaretRowChanged(object? sender, PropertyChangedEventArgs e) { MarkDirty(); RaiseCaretPreview(); }
    private void OnMouseRowChanged(object? sender, PropertyChangedEventArgs e) { MarkDirty(); RaiseMousePreview(); }

    private void OnCaretRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RewireRows(e, OnCaretRowChanged);
        MarkDirty();
        RaiseCaretPreview();
        RaisePropertyChanged(nameof(CaretHasColors));
    }

    private void OnMouseRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RewireRows(e, OnMouseRowChanged);
        MarkDirty();
        RaiseMousePreview();
        RaisePropertyChanged(nameof(MouseHasColors));
    }

    private static void RewireRows(NotifyCollectionChangedEventArgs e, PropertyChangedEventHandler handler)
    {
        if (e.NewItems != null)
            foreach (LabelStyleRow r in e.NewItems) r.PropertyChanged += handler;
        if (e.OldItems != null)
            foreach (LabelStyleRow r in e.OldItems) r.PropertyChanged -= handler;
    }

    /// <summary>True when the active UI language flows right-to-left (e.g. Hebrew). Bound by the
    /// Idle-reset preview to mirror its arrow glyph; the caret/mouse previews stay LTR regardless.</summary>
    public bool IsRtl => string.Equals(_localization.CurrentLanguage.FlowDirection, "RightToLeft", StringComparison.OrdinalIgnoreCase);

    // ---- Add-language picker catalog ----
    // Shared by all three color tables (caret/mouse/strip). Built from the framework's neutral
    // cultures so the list is comprehensive and self-localizing, with the layouts actually
    // installed on this machine sorted and grouped first (that's almost always what the user wants).
    public ObservableCollection<LanguageOption> LanguageOptions { get; }

    private ObservableCollection<LanguageOption> BuildLanguageOptions()
    {
        var installed = new HashSet<string>(
            _layout.GetInstalledLayouts().Select(l => l.TwoLetterCode.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        var groupInstalled = _localization.T("Lang_GroupInstalled");
        var groupAll = _localization.T("Lang_GroupAll");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new List<LanguageOption>();
        foreach (var c in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            var code = c.TwoLetterISOLanguageName?.ToLowerInvariant();
            // "iv" is the invariant culture — not a real language the user would tag a layout with.
            if (string.IsNullOrEmpty(code) || code == "iv") continue;
            if (!seen.Add(code)) continue;
            var isInstalled = installed.Contains(code);
            options.Add(new LanguageOption(
                code,
                CapitalizeFirst(c.NativeName),
                c.EnglishName,
                isInstalled,
                isInstalled ? groupInstalled : groupAll));
        }

        var ordered = options
            .OrderByDescending(o => o.IsInstalled)
            .ThenBy(o => o.EnglishName, StringComparer.CurrentCultureIgnoreCase);
        return new ObservableCollection<LanguageOption>(ordered);
    }

    // Several languages' NativeName comes back lowercased (e.g. "עברית" is fine, but many Latin
    // scripts return "español"); title-casing the first letter keeps the picker tidy.
    private static string CapitalizeFirst(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

    // ---- Strip ----
    private bool _stripEnabled;
    public bool StripEnabled { get => _stripEnabled; set => SetProperty(ref _stripEnabled, value); }

    public string[] StripThicknesses { get; } = new[] { "small", "medium", "large", "full" };
    private string _stripThickness;
    public string StripThickness
    {
        get => _stripThickness;
        set
        {
            if (SetProperty(ref _stripThickness, value))
            {
                RaisePropertyChanged(nameof(StripVerticalPositionEnabled));
                RaisePropertyChanged(nameof(StripThicknessPixels));
                RaisePropertyChanged(nameof(StripFillsTaskbar));
            }
        }
    }

    public string[] StripVerticalPositions { get; } = new[] { "top", "bottom" };
    private string _stripVerticalPosition;
    public string StripVerticalPosition { get => _stripVerticalPosition; set => SetProperty(ref _stripVerticalPosition, value); }

    private bool _stripOpacityEnabled;
    public bool StripOpacityEnabled
    {
        get => _stripOpacityEnabled;
        set { if (SetProperty(ref _stripOpacityEnabled, value)) RaisePropertyChanged(nameof(EffectiveStripOpacity)); }
    }

    private double _stripOpacity;
    public double StripOpacity
    {
        get => _stripOpacity;
        set { if (SetProperty(ref _stripOpacity, Math.Clamp(value, 0.0, 1.0))) RaisePropertyChanged(nameof(EffectiveStripOpacity)); }
    }

    /// <summary>Effective opacity applied to the preview swatch: ignores the slider when transparency is disabled.</summary>
    public double EffectiveStripOpacity => _stripOpacityEnabled ? _stripOpacity : 1.0;

    /// <summary>Approximate pixel height the strip occupies on the preview taskbar (small/medium/large).</summary>
    public int StripThicknessPixels => _stripThickness?.ToLowerInvariant() switch
    {
        "small" => 4,
        "medium" => 7,
        "large" => 11,
        "full" => 22, // taskbar-fill preview height
        _ => 4,
    };

    /// <summary>True when the strip covers the full taskbar height — the preview hides the vertical-position UI in that case.</summary>
    public bool StripFillsTaskbar => string.Equals(_stripThickness, "full", StringComparison.OrdinalIgnoreCase);

    public string[] StripPlacements { get; } = new[] { "front", "behind" };
    private string _stripPlacement;
    public string StripPlacement { get => _stripPlacement; set => SetProperty(ref _stripPlacement, value); }

    public bool StripVerticalPositionEnabled =>
        !string.Equals(_stripThickness, "full", StringComparison.OrdinalIgnoreCase);

    public ObservableCollection<LangColorRow> ColorRows { get; }

    private void ReloadColorRows()
    {
        ColorRows.Clear();
        foreach (var kv in _settings.Current.TaskbarStrip.Colors)
            ColorRows.Add(new LangColorRow { Code = kv.Key, Color = kv.Value });
    }

    private void AddStripColor(string? code)
    {
        var c = (code ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(c)) return;
        foreach (var r in ColorRows)
            if (string.Equals((r.Code ?? "").Trim(), c, StringComparison.OrdinalIgnoreCase)) return;
        ColorRows.Add(new LangColorRow { Code = c, Color = "#3B82F6" });
    }

    private void RemoveColorRow(LangColorRow? row) { if (row != null) ColorRows.Remove(row); }

    /// <summary>Language code shown inside the preview cards. Uses the first configured row, or "EN" as a fallback.</summary>
    public string PreviewCode
    {
        get
        {
            foreach (var row in ColorRows)
            {
                var code = (row.Code ?? "").Trim();
                if (!string.IsNullOrEmpty(code)) return code.ToUpperInvariant();
            }
            return "EN";
        }
    }

    /// <summary>Color used by the strip preview swatch. Uses the first configured row, or a neutral gray fallback.</summary>
    public string PreviewColor
    {
        get
        {
            foreach (var row in ColorRows)
            {
                var c = (row.Color ?? "").Trim();
                if (!string.IsNullOrEmpty(c)) return c;
            }
            return "#3B82F6";
        }
    }

    // ---- Idle ----
    private bool _idleEnabled;
    public bool IdleEnabled { get => _idleEnabled; set => SetProperty(ref _idleEnabled, value); }
    private int _idleSeconds;
    // Floor at 5s (same lower bound used in Apply()) and cap at a week — beyond that the user
    // has effectively disabled the feature, so it might as well be expressed via IdleEnabled.
    public int IdleSeconds { get => _idleSeconds; set => SetProperty(ref _idleSeconds, Math.Clamp(value, 5, 7 * 24 * 60 * 60)); }
    private string _idleTarget;
    public string IdleTarget { get => _idleTarget; set => SetProperty(ref _idleTarget, value); }

    public ObservableCollection<string> AvailableCodes
    {
        get
        {
            var list = _layout.GetInstalledLayouts().Select(l => l.TwoLetterCode.ToLowerInvariant()).Distinct().OrderBy(x => x);
            return new ObservableCollection<string>(list);
        }
    }

    // ---- General ----
    private bool _autostart;
    public bool Autostart { get => _autostart; set => SetProperty(ref _autostart, value); }

    // ---- About ----
    // Read once from the running assembly so the metadata baked in by the .csproj (<Version>,
    // <Copyright>) is the single source of truth — the About card never hardcodes a version.

    /// <summary>App version shown on the About card, e.g. "1.0.0". Sourced from
    /// AssemblyInformationalVersion (falls back to the assembly version, then "—").</summary>
    public string AppVersion { get; } = ResolveAppVersion();

    /// <summary>Copyright line shown on the About card. Sourced from AssemblyCopyright, set by
    /// the &lt;Copyright&gt; csproj property.</summary>
    public string AppCopyright { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    private static string ResolveAppVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            // .NET appends a "+<git-sha>" source-revision suffix to InformationalVersion; trim it
            // so the UI shows a clean "1.0.0" rather than "1.0.0+a1b2c3d".
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
        return asm.GetName().Version?.ToString() ?? "—";
    }

    // ---- Language ----
    public ObservableCollection<LanguagePack> AvailableLanguages { get; }

    private LanguagePack? _selectedLanguage;
    /// <summary>Selected language pack object — bound to the language ComboBox in the General tab.
    /// May be null if no packs were discovered (degraded mode); the UI just disables the combo.</summary>
    public LanguagePack? SelectedLanguage
    {
        get => _selectedLanguage;
        set => SetProperty(ref _selectedLanguage, value);
    }

    private LanguagePack? ResolveSelectedLanguage(string code)
    {
        if (AvailableLanguages.Count == 0) return null;
        foreach (var p in AvailableLanguages)
            if (string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase)) return p;
        // Fall back to whatever the localization service decided is current — keeps the combo
        // in sync with what's actually rendering, even when settings hold a stale/unknown code.
        foreach (var p in AvailableLanguages)
            if (string.Equals(p.Code, _localization.CurrentLanguage.Code, StringComparison.OrdinalIgnoreCase)) return p;
        return AvailableLanguages[0];
    }

    // ---- Dirty tracking ----
    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    private void MarkDirty() { if (_initialized && !_isDirty) IsDirty = true; }

    /// <summary>
    /// Clears the dirty flag. The view calls this once after Loaded so that any binding-init
    /// noise (editable ComboBox bouncing, NumberBox round-tripping) doesn't mark the VM dirty
    /// before the user has touched anything.
    /// </summary>
    public void ResetDirty() => IsDirty = false;

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsDirty)) return;
        // Computed properties — their source already marks dirty when it changes.
        switch (e.PropertyName)
        {
            case nameof(StripVerticalPositionEnabled):
            case nameof(StripThicknessPixels):
            case nameof(StripFillsTaskbar):
            case nameof(EffectiveStripOpacity):
            case nameof(PreviewCode):
            case nameof(PreviewColor):
            case nameof(CaretPreviewCode):
            case nameof(CaretPreviewBackground):
            case nameof(CaretPreviewForeground):
            case nameof(CaretPreviewBorderBrush):
            case nameof(CaretPreviewBorderThickness):
            case nameof(CaretPreviewOpacity):
            case nameof(MousePreviewCode):
            case nameof(MousePreviewBackground):
            case nameof(MousePreviewForeground):
            case nameof(MousePreviewBorderBrush):
            case nameof(MousePreviewBorderThickness):
            case nameof(MousePreviewOpacity):
                return;
        }
        MarkDirty();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkDirty();
        RaisePropertyChanged(nameof(PreviewCode));
        RaisePropertyChanged(nameof(PreviewColor));
    }

    private void OnColorRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (LangColorRow r in e.NewItems) r.PropertyChanged += OnRowChanged;
        if (e.OldItems != null)
            foreach (LangColorRow r in e.OldItems) r.PropertyChanged -= OnRowChanged;
        MarkDirty();
        RaisePropertyChanged(nameof(PreviewCode));
        RaisePropertyChanged(nameof(PreviewColor));
    }

    public ICommand SaveCommand { get; }
    public ICommand DiscardCommand { get; }
    public ICommand AddColorCommand { get; }
    public ICommand RemoveColorCommand { get; }
    public ICommand AddCaretColorCommand { get; }
    public ICommand RemoveCaretColorCommand { get; }
    public ICommand AddMouseColorCommand { get; }
    public ICommand RemoveMouseColorCommand { get; }

    /// <summary>
    /// Seeds every editable field (and the three color tables) from the persisted settings.
    /// Called once from the constructor and again by <see cref="Discard"/>; the row collections
    /// are repopulated in-place so their CollectionChanged subscriptions stay intact.
    /// </summary>
    private void LoadFromSettings()
    {
        var s = _settings.Current;

        // Pre-select the saved language pack object, not just its code, so the ComboBox can
        // bind to SelectedItem (richer than SelectedValue — gives us access to NativeName etc.)
        _selectedLanguage = ResolveSelectedLanguage(s.General.Language);

        _caretEnabled = s.CaretLabel.Enabled;
        _caretOffsetX = s.CaretLabel.OffsetX;
        _caretOffsetY = s.CaretLabel.OffsetY;
        _caretFont = s.CaretLabel.Font;
        _caretSize = s.CaretLabel.Size;
        _caretOpacity = Math.Clamp(s.CaretLabel.Opacity, 0.0, 1.0);

        _mouseEnabled = s.MouseLabel.Enabled;
        _mouseOffsetX = s.MouseLabel.OffsetX;
        _mouseOffsetY = s.MouseLabel.OffsetY;
        _mouseFont = s.MouseLabel.Font;
        _mouseSize = s.MouseLabel.Size;
        _mouseOpacity = Math.Clamp(s.MouseLabel.Opacity, 0.0, 1.0);
        _mouseTrackingMode = NormalizeTrackingMode(s.MouseLabel.TrackingMode);

        ReloadLabelRows(CaretColorRows, s.CaretLabel.Colors);
        ReloadLabelRows(MouseColorRows, s.MouseLabel.Colors);

        _stripEnabled = s.TaskbarStrip.Enabled;
        _stripThickness = s.TaskbarStrip.Thickness;
        _stripVerticalPosition = s.TaskbarStrip.VerticalPosition;
        _stripOpacityEnabled = s.TaskbarStrip.OpacityEnabled;
        _stripOpacity = s.TaskbarStrip.Opacity;
        _stripPlacement = s.TaskbarStrip.Placement;
        ReloadColorRows();

        _idleEnabled = s.IdleReset.Enabled;
        _idleSeconds = s.IdleReset.IdleSeconds;
        _idleTarget = s.IdleReset.TargetLang;

        _autostart = _startup.IsEnabled();
    }

    /// <summary>
    /// Throws away every unsaved edit by re-seeding from the last persisted settings, then clears
    /// the dirty flag. Nothing is written to disk and the window stays open, so the user can keep
    /// working from a clean slate. The blanket change notification re-evaluates all bindings
    /// (scalar fields, previews and the repopulated color tables) at once.
    /// </summary>
    public void Discard()
    {
        if (!IsDirty) return;

        LoadFromSettings();

        // Refresh every binding in one shot — empty name means "all properties changed" in WPF.
        // Repopulating the row collections above already raised their per-table dirty/preview
        // notifications, so resetting IsDirty last lands the VM back on a clean slate.
        RaisePropertyChanged(string.Empty);
        IsDirty = false;
    }

    /// <summary>
    /// Projects VM state into settings and persists. Does NOT close the window.
    /// Any persistence error is routed through <see cref="IErrorReporter"/> so the caller (the
    /// Closing dialog handler) sees <see cref="IsDirty"/> still set and can keep the window open
    /// instead of vanishing the user's edits.
    /// </summary>
    public void Apply()
    {
        try { ApplyCore(); }
        catch (Exception ex)
        {
            _reporter.Report("SettingsViewModel.Apply", ex);
            // Leave IsDirty as-is so the OnClosing handler in SettingsWindow re-prompts/stays open.
        }
    }

    private void ApplyCore()
    {
        var s = _settings.Current;
        s.CaretLabel.Enabled = CaretEnabled;
        s.CaretLabel.OffsetX = CaretOffsetX;
        s.CaretLabel.OffsetY = CaretOffsetY;
        s.CaretLabel.Font = CaretFont;
        s.CaretLabel.Size = CaretSize;
        s.CaretLabel.Opacity = Math.Clamp(CaretOpacity, 0.0, 1.0);
        ApplyLabelRows(CaretColorRows, s.CaretLabel.Colors);

        s.MouseLabel.Enabled = MouseEnabled;
        s.MouseLabel.OffsetX = MouseOffsetX;
        s.MouseLabel.OffsetY = MouseOffsetY;
        s.MouseLabel.Font = MouseFont;
        s.MouseLabel.Size = MouseSize;
        s.MouseLabel.Opacity = Math.Clamp(MouseOpacity, 0.0, 1.0);
        s.MouseLabel.TrackingMode = NormalizeTrackingMode(MouseTrackingMode);
        ApplyLabelRows(MouseColorRows, s.MouseLabel.Colors);

        s.TaskbarStrip.Enabled = StripEnabled;
        s.TaskbarStrip.Thickness = StripThickness ?? "small";
        s.TaskbarStrip.VerticalPosition = StripVerticalPosition ?? "top";
        s.TaskbarStrip.OpacityEnabled = StripOpacityEnabled;
        s.TaskbarStrip.Opacity = Math.Clamp(StripOpacity, 0.0, 1.0);
        s.TaskbarStrip.Placement = StripPlacement ?? "front";
        s.TaskbarStrip.Colors.Clear();
        foreach (var row in ColorRows)
        {
            var code = (row.Code ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(code)) continue;
            s.TaskbarStrip.Colors[code] = row.Color;
        }

        s.IdleReset.Enabled = IdleEnabled;
        s.IdleReset.IdleSeconds = Math.Max(5, IdleSeconds);
        s.IdleReset.TargetLang = (IdleTarget ?? "en").ToLowerInvariant();

        s.General.Autostart = Autostart;

        // Persist the chosen language code (not the whole pack — only the code is stable across
        // disk/runtime) and switch the active translation dictionary immediately so the user
        // sees the new language without closing/reopening the window.
        var chosenCode = SelectedLanguage?.Code ?? "";
        if (s.General.Language != chosenCode)
        {
            s.General.Language = chosenCode;
            _localization.SetLanguage(chosenCode);
            // The UI language may have flipped LTR/RTL — refresh the idle-preview arrow direction.
            RaisePropertyChanged(nameof(IsRtl));
        }

        _settings.Save();
        _startup.SetEnabled(Autostart);

        IsDirty = false;
    }
}

public sealed class LangColorRow : BindableBase
{
    private string _code = "";
    private string _color = "#888888";
    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public string Color { get => _color; set => SetProperty(ref _color, value); }
}

/// <summary>One editable row in a caret/mouse label color table: a language code mapped to the
/// chip's background, text and border appearance. <see cref="BorderThickness"/> of 0 = no border.</summary>
public sealed class LabelStyleRow : BindableBase
{
    private string _code = "";
    private string _background = "#CC222222";
    private string _foreground = "#FFFFFFFF";
    private string _borderColor = "#FF000000";
    private double _borderThickness;
    public string Code { get => _code; set => SetProperty(ref _code, value); }
    public string Background { get => _background; set => SetProperty(ref _background, value); }
    public string Foreground { get => _foreground; set => SetProperty(ref _foreground, value); }
    public string BorderColor { get => _borderColor; set => SetProperty(ref _borderColor, value); }
    public double BorderThickness
    {
        get => _borderThickness;
        set { if (SetProperty(ref _borderThickness, value)) RaisePropertyChanged(nameof(BorderThicknessUniform)); }
    }

    /// <summary>Uniform <see cref="Thickness"/> projection of <see cref="BorderThickness"/> for the
    /// swatch's BorderThickness binding (Border expects a Thickness, not a bare double).</summary>
    public Thickness BorderThicknessUniform => new(_borderThickness);
}
