using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using OopsType.Infrastructure;
using OopsType.Models.Localization;

namespace OopsType.Services.Localization;

/// <summary>
/// Default <see cref="ILocalizationService"/>. Discovers language packs from two locations on
/// startup, loads them all into memory, and publishes the active one as a
/// <see cref="ResourceDictionary"/> merged into <see cref="Application.Resources"/>.
///
/// <para>Discovery order (later overrides earlier on duplicate <c>Code</c>):</para>
/// <list type="number">
///   <item><c>&lt;exe-dir&gt;\Languages\*.json</c> — packs that ship with the binary.</item>
///   <item><c>%LOCALAPPDATA%\OopsType\Languages\*.json</c> — user-added packs. Lets the user
///   add languages or override built-ins without touching the install directory (which may be
///   read-only when installed to Program Files).</item>
/// </list>
///
/// <para>Fallback chain at lookup time:</para>
/// <list type="number">
///   <item>String defined in the active language pack.</item>
///   <item>String defined in the default (English) pack — applied by merging the default pack
///   under the active one when populating the resource dictionary.</item>
///   <item>The raw key itself, so a missing entry is obvious in the UI rather than blank.</item>
/// </list>
///
/// <para>Switching language replaces the merged dictionary; every <c>{DynamicResource Key}</c>
/// (or <c>{loc:T Key}</c>, which is the same under the hood) re-resolves and refreshes the UI
/// live, without a restart.</para>
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    /// <summary>Subdirectory name searched in both the exe folder and LOCALAPPDATA.</summary>
    private const string LanguagesFolderName = "Languages";

    /// <summary>Code used as the fallback layer beneath any active language.</summary>
    private const string DefaultLanguageCode = "en";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IErrorReporter _reporter;
    private readonly Dictionary<string, LanguagePack> _packsByCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly LanguagePack _emptyFallback = new() { Code = "??", Name = "(none)", NativeName = "(none)" };

    private LanguagePack _current;
    private ResourceDictionary? _activeDictionary;

    public IReadOnlyList<LanguagePack> AvailableLanguages { get; private set; } = Array.Empty<LanguagePack>();
    public LanguagePack CurrentLanguage => _current;
    public event Action? LanguageChanged;

    public LocalizationService(IErrorReporter reporter)
    {
        _reporter = reporter;
        _current = _emptyFallback;

        DiscoverPacks();

        // If discovery turned up nothing (no files, all corrupt), keep the empty fallback so the
        // app still runs — every T() call will just echo the key.
        if (_packsByCode.Count == 0) return;

        // Order: prefer the default language first, then everything else by display name.
        // The list is shown in the settings combo box exactly as ordered here.
        AvailableLanguages = _packsByCode.Values
            .OrderByDescending(p => string.Equals(p.Code, DefaultLanguageCode, StringComparison.OrdinalIgnoreCase))
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void SetLanguage(string code)
    {
        var pack = ResolvePack(code);
        _current = pack;

        ApplyToApplicationResources(pack);

        try { LanguageChanged?.Invoke(); }
        catch (Exception ex) { _reporter.Report("LocalizationService.LanguageChanged", ex); }
    }

    public string T(string key)
    {
        if (string.IsNullOrEmpty(key)) return key ?? string.Empty;

        if (_current.Strings.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
            return v;

        // Fallback to default pack if the active one is missing the key — same chain the
        // resource dictionary uses, so code-side lookups stay consistent with XAML bindings.
        if (_packsByCode.TryGetValue(DefaultLanguageCode, out var def)
            && def.Strings.TryGetValue(key, out var dv)
            && !string.IsNullOrEmpty(dv))
            return dv;

        return key;
    }

    private LanguagePack ResolvePack(string code)
    {
        if (!string.IsNullOrEmpty(code) && _packsByCode.TryGetValue(code, out var p)) return p;
        if (_packsByCode.TryGetValue(DefaultLanguageCode, out var def)) return def;
        return AvailableLanguages.FirstOrDefault() ?? _emptyFallback;
    }

    private void DiscoverPacks()
    {
        foreach (var dir in GetSearchDirectories())
            LoadPacksFrom(dir);
    }

    private IEnumerable<string> GetSearchDirectories()
    {
        // Built-in packs alongside the .exe (shipped via the csproj Content rule).
        // Use AppContext.BaseDirectory rather than Assembly.GetEntryAssembly().Location:
        // under single-file publishing (PublishSingleFile=true) the entry assembly has no
        // on-disk location and Location returns "", which would leave the Languages folder
        // unsearched and the whole UI blank. BaseDirectory resolves correctly in both the
        // single-file and normal-build cases.
        string? exeDir = null;
        try { exeDir = AppContext.BaseDirectory; }
        catch (Exception ex) { _reporter.Report("LocalizationService.ExeDir", ex); }

        if (!string.IsNullOrEmpty(exeDir))
            yield return Path.Combine(exeDir, LanguagesFolderName);

        // User-added packs in LOCALAPPDATA — listed after exeDir so duplicates here win the lookup.
        string? userDir = null;
        try
        {
            userDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OopsType", LanguagesFolderName);
        }
        catch (Exception ex) { _reporter.Report("LocalizationService.UserDir", ex); }

        if (!string.IsNullOrEmpty(userDir))
            yield return userDir;
    }

    private void LoadPacksFrom(string directory)
    {
        if (!Directory.Exists(directory)) return;

        string[] files;
        try { files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly); }
        catch (Exception ex) { _reporter.Report($"LocalizationService.Enumerate/{directory}", ex); return; }

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var pack = JsonSerializer.Deserialize<LanguagePack>(json, JsonOptions);
                if (pack == null || string.IsNullOrWhiteSpace(pack.Code))
                {
                    _reporter.Report($"LocalizationService.Load/{Path.GetFileName(file)}",
                        new InvalidDataException("Language file has no 'code' field; skipped."));
                    continue;
                }

                // Later directories win — that's the user-override semantics promised in the
                // class summary. Indexer assignment replaces the previous entry.
                _packsByCode[pack.Code] = pack;
            }
            catch (Exception ex)
            {
                _reporter.Report($"LocalizationService.Load/{Path.GetFileName(file)}", ex);
            }
        }
    }

    private void ApplyToApplicationResources(LanguagePack pack)
    {
        var app = Application.Current;
        if (app == null) return;

        // Remove the previous translations dictionary (if any) before adding the new one — keeps
        // the merged-dictionary list short and prevents stale keys from a previous language
        // shadowing a delete in the new pack. We null out the reference unconditionally: if Remove
        // throws (rare, but it has been observed when WPF's dictionary list is in an inconsistent
        // state mid-shutdown), retaining the field would mean we never even try to drop it again
        // and the merged-dictionary list grows by one entry per language switch, slowing every
        // {DynamicResource} resolution forever.
        if (_activeDictionary != null)
        {
            try { app.Resources.MergedDictionaries.Remove(_activeDictionary); }
            catch (Exception ex) { _reporter.Report("LocalizationService.RemoveOldDict", ex); }
            finally { _activeDictionary = null; }
        }

        var dict = new ResourceDictionary();

        // Default pack first so the active pack overrides it key-by-key — anything the active
        // pack doesn't define falls back to English automatically through the same dictionary.
        if (_packsByCode.TryGetValue(DefaultLanguageCode, out var def) && def != pack)
            foreach (var kv in def.Strings) dict[kv.Key] = kv.Value ?? string.Empty;

        foreach (var kv in pack.Strings) dict[kv.Key] = kv.Value ?? string.Empty;

        app.Resources.MergedDictionaries.Add(dict);
        _activeDictionary = dict;
    }
}
