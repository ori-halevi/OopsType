using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly bool _initialized;

    public SettingsViewModel(ISettingsService settings, IKeyboardLayoutService layout, IStartupService startup, ILocalizationService localization)
    {
        _settings = settings;
        _layout = layout;
        _startup = startup;
        _localization = localization;

        AvailableLanguages = new ObservableCollection<LanguagePack>(_localization.AvailableLanguages);
        // Pre-select the saved language pack object, not just its code, so the ComboBox can
        // bind to SelectedItem (richer than SelectedValue — gives us access to NativeName etc.)
        _selectedLanguage = ResolveSelectedLanguage(settings.Current.General.Language);

        var s = _settings.Current;
        _caretEnabled = s.CaretLabel.Enabled;
        _caretOffsetX = s.CaretLabel.OffsetX;
        _caretOffsetY = s.CaretLabel.OffsetY;
        _caretFont = s.CaretLabel.Font;
        _caretSize = s.CaretLabel.Size;

        _mouseEnabled = s.MouseLabel.Enabled;
        _mouseOffsetX = s.MouseLabel.OffsetX;
        _mouseOffsetY = s.MouseLabel.OffsetY;
        _mouseFont = s.MouseLabel.Font;
        _mouseSize = s.MouseLabel.Size;
        _mouseTrackingMode = NormalizeTrackingMode(s.MouseLabel.TrackingMode);

        _stripEnabled = s.TaskbarStrip.Enabled;
        _stripThickness = s.TaskbarStrip.Thickness;
        _stripVerticalPosition = s.TaskbarStrip.VerticalPosition;
        _stripOpacityEnabled = s.TaskbarStrip.OpacityEnabled;
        _stripOpacity = s.TaskbarStrip.Opacity;
        _stripPlacement = s.TaskbarStrip.Placement;
        ColorRows = new ObservableCollection<LangColorRow>();
        ReloadColorRows();

        _idleEnabled = s.IdleReset.Enabled;
        _idleSeconds = s.IdleReset.IdleSeconds;
        _idleTarget = s.IdleReset.TargetLang;

        _autostart = _startup.IsEnabled();

        SaveCommand = new DelegateCommand(Apply, () => IsDirty).ObservesProperty(() => IsDirty);
        AddColorCommand = new DelegateCommand(AddColorRow);
        RemoveColorCommand = new DelegateCommand<LangColorRow>(RemoveColorRow);

        // ---- dirty tracking ----
        // Subscribe AFTER all backing fields are seeded so initial assignments don't mark dirty.
        ColorRows.CollectionChanged += OnColorRowsChanged;
        foreach (var row in ColorRows) row.PropertyChanged += OnRowChanged;
        PropertyChanged += OnVmPropertyChanged;
        _initialized = true;
    }

    // System-installed font families — computed once per VM instance (cheap, but the list never changes mid-session).
    public IReadOnlyList<string> AvailableFonts { get; } = Fonts.SystemFontFamilies
        .Select(f => f.Source)
        .Distinct()
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    // ---- Caret label (follows text caret) ----
    private bool _caretEnabled;
    public bool CaretEnabled { get => _caretEnabled; set => SetProperty(ref _caretEnabled, value); }

    private int _caretOffsetX;
    public int CaretOffsetX { get => _caretOffsetX; set => SetProperty(ref _caretOffsetX, value); }
    private int _caretOffsetY;
    public int CaretOffsetY { get => _caretOffsetY; set => SetProperty(ref _caretOffsetY, value); }
    private string _caretFont;
    public string CaretFont { get => _caretFont; set => SetProperty(ref _caretFont, value); }
    private int _caretSize;
    public int CaretSize { get => _caretSize; set => SetProperty(ref _caretSize, value); }

    // ---- Mouse label (follows mouse cursor) ----
    private bool _mouseEnabled;
    public bool MouseEnabled { get => _mouseEnabled; set => SetProperty(ref _mouseEnabled, value); }
    private int _mouseOffsetX;
    public int MouseOffsetX { get => _mouseOffsetX; set => SetProperty(ref _mouseOffsetX, value); }
    private int _mouseOffsetY;
    public int MouseOffsetY { get => _mouseOffsetY; set => SetProperty(ref _mouseOffsetY, value); }
    private string _mouseFont;
    public string MouseFont { get => _mouseFont; set => SetProperty(ref _mouseFont, value); }
    private int _mouseSize;
    public int MouseSize { get => _mouseSize; set => SetProperty(ref _mouseSize, value); }

    public string[] MouseTrackingModes { get; } = new[] { "economy", "max-smoothness" };
    private string _mouseTrackingMode;
    public string MouseTrackingMode { get => _mouseTrackingMode; set => SetProperty(ref _mouseTrackingMode, value); }

    private static string NormalizeTrackingMode(string? mode) =>
        string.Equals(mode, "max-smoothness", StringComparison.OrdinalIgnoreCase) ? "max-smoothness" : "economy";

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

    private void AddColorRow() => ColorRows.Add(new LangColorRow { Code = "", Color = "#888888" });
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
    public int IdleSeconds { get => _idleSeconds; set => SetProperty(ref _idleSeconds, value); }
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
    public ICommand AddColorCommand { get; }
    public ICommand RemoveColorCommand { get; }

    /// <summary>Projects VM state into settings and persists. Does NOT close the window.</summary>
    public void Apply()
    {
        var s = _settings.Current;
        s.CaretLabel.Enabled = CaretEnabled;
        s.CaretLabel.OffsetX = CaretOffsetX;
        s.CaretLabel.OffsetY = CaretOffsetY;
        s.CaretLabel.Font = CaretFont;
        s.CaretLabel.Size = CaretSize;

        s.MouseLabel.Enabled = MouseEnabled;
        s.MouseLabel.OffsetX = MouseOffsetX;
        s.MouseLabel.OffsetY = MouseOffsetY;
        s.MouseLabel.Font = MouseFont;
        s.MouseLabel.Size = MouseSize;
        s.MouseLabel.TrackingMode = NormalizeTrackingMode(MouseTrackingMode);

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
