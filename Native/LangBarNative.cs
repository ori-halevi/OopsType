using System;
using System.Runtime.InteropServices;

namespace OopsType.Native;

/// <summary>
/// Minimal Text Services Framework (TSF) interop used to show/hide the Windows language bar.
/// Only the pieces <see cref="OopsType.Services.LanguageBarService"/> needs are declared.
/// </summary>
internal static class LangBarNative
{
    // TF_SFT_* floating-language-bar display flags for ITfLangBarMgr::ShowFloating (ctfutb.h).
    // Only the two we drive are declared; the full set is documented under "TF_SFT_ Constants".
    public const uint TF_SFT_SHOWNORMAL = 0x00000001;
    public const uint TF_SFT_HIDDEN = 0x00000008;

    // Exported by msctf.dll. Creates the per-thread language bar manager:
    //   HRESULT WINAPI TF_CreateLangBarMgr(ITfLangBarMgr **pppbm);
    // Must be called on an STA thread with a message pump (the WPF UI thread qualifies).
    [DllImport("msctf.dll")]
    public static extern int TF_CreateLangBarMgr(out ITfLangBarMgr pppbm);
}

/// <summary>
/// TSF language bar manager (ctfutb.h, IID 87955690-e627-11d2-8ddb-00105a2799b5).
/// Methods are declared in EXACT vtable order so the slots line up — only
/// <see cref="ShowFloating"/> is actually invoked; the earlier members are placeholders
/// (typed loosely with IntPtr) whose sole job is to preserve the vtable layout.
/// </summary>
[ComImport]
[Guid("87955690-e627-11d2-8ddb-00105a2799b5")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfLangBarMgr
{
    [PreserveSig] int AdviseEventSink(IntPtr pSink, IntPtr hwnd, uint dwflags, out uint pdwCookie);
    [PreserveSig] int UnAdviseEventSink(uint dwCookie);
    [PreserveSig] int GetThreadMarshalInterface(uint dwThreadId, uint dwType, ref Guid riid, out IntPtr ppunk);
    [PreserveSig] int GetThreadLangBarItemMgr(uint dwThreadId, out IntPtr pplbie, out uint pdwThreadid);
    [PreserveSig] int GetInputProcessorProfiles(uint dwThreadId, out IntPtr ppaip, out uint pdwThreadid);
    [PreserveSig] int RestoreLastFocus(out uint dwThreadId, [MarshalAs(UnmanagedType.Bool)] bool fPrev);
    [PreserveSig] int SetModalInput(IntPtr pSink, uint dwThreadId, uint dwFlags);

    /// <summary>Sets the floating language bar display state to a logical OR of TF_SFT_* flags.</summary>
    [PreserveSig] int ShowFloating(uint dwFlags);

    /// <summary>Retrieves the current floating language bar display state (TF_SFT_* flags).</summary>
    [PreserveSig] int GetShowFloatingStatus(out uint pdwFlags);
}
