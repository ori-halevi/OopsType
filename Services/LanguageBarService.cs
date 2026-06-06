using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OopsType.Infrastructure;
using OopsType.Native;

namespace OopsType.Services;

/// <summary>
/// Default <see cref="ILanguageBarService"/>. Hides/restores the Windows language indicator using
/// two cooperating layers:
///
/// <para>1. <b>Persisted truth</b> — <c>HKCU\Software\Microsoft\CTF\LangBar\ShowStatus</c>:
///   <see cref="ShowStatusHidden"/> (3) = the "use desktop language bar + Hidden" combination
///   (no indicator anywhere), <see cref="ShowStatusNormal"/> (0) = the default modern taskbar
///   input indicator. Writing this guarantees the choice survives a reboot, since ctfmon reads
///   ShowStatus at logon. Both values were captured empirically by diffing the registry around the
///   manual Windows "Advanced keyboard settings → Language bar → Hidden" toggle.</para>
///
/// <para>2. <b>Live application</b> — ctfmon only re-reads ShowStatus at logon, so a registry write
///   alone would not take effect until the next sign-in. To apply it immediately we drive the
///   documented TSF COM API <see cref="ITfLangBarMgr.ShowFloating"/> with the TF_SFT_* flags.</para>
///
/// Every registry/COM call is wrapped and routed through <see cref="IErrorReporter"/>: on a managed
/// or locked-down device these can throw (or msctf.dll may be unavailable), and a tray utility must
/// degrade quietly rather than crash. If only the live nudge fails, the registry write still applies
/// the choice on the next sign-in.
/// </summary>
public sealed class LanguageBarService : ILanguageBarService
{
    private const string LangBarKey = @"Software\Microsoft\CTF\LangBar";
    private const string ShowStatusValue = "ShowStatus";

    // ShowStatus encodings observed from the Windows "Text Services and Input Languages" dialog.
    private const int ShowStatusNormal = 0; // modern taskbar input indicator (Windows default)
    private const int ShowStatusHidden = 3; // desktop language bar enabled + set to Hidden

    private readonly IErrorReporter _reporter;

    public LanguageBarService(IErrorReporter reporter) => _reporter = reporter;

    public bool IsHidden()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LangBarKey, writable: false);
            // GetValue returns the DWORD boxed as int; anything else (missing key/value) ⇒ "shown".
            return key?.GetValue(ShowStatusValue) is int status && status == ShowStatusHidden;
        }
        catch (Exception ex)
        {
            _reporter.Report("LanguageBarService.IsHidden", ex);
            return false;
        }
    }

    public void SetHidden(bool hidden)
    {
        // 1) Persist first, so the choice survives a reboot even if the live nudge below fails.
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(LangBarKey, writable: true);
            key?.SetValue(ShowStatusValue, hidden ? ShowStatusHidden : ShowStatusNormal, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            _reporter.Report("LanguageBarService.SetHidden/registry", ex);
        }

        // 2) Apply live so the indicator appears/disappears now, not at the next sign-in.
        ApplyLive(hidden);
    }

    private void ApplyLive(bool hidden)
    {
        ITfLangBarMgr? mgr = null;
        try
        {
            // S_OK == 0. A non-zero HRESULT or null manager means TSF is unavailable — the
            // registry write already covers persistence, so just bail.
            if (LangBarNative.TF_CreateLangBarMgr(out mgr) != 0 || mgr is null) return;
            mgr.ShowFloating(hidden ? LangBarNative.TF_SFT_HIDDEN : LangBarNative.TF_SFT_SHOWNORMAL);
        }
        catch (Exception ex)
        {
            _reporter.Report("LanguageBarService.ApplyLive", ex);
        }
        finally
        {
            if (mgr != null) Marshal.ReleaseComObject(mgr);
        }
    }
}
