; ─────────────────────────────────────────────────────────────────────────────
;  OopsType — Inno Setup script
;
;  Packages the self-contained, single-file publish output into a Windows installer.
;  Compile with Inno Setup 6 (ISCC.exe OopsType.iss) or the Inno Setup Compiler GUI.
;
;  Notes on this app:
;   * Self-contained win-x64 single-file build — the .NET runtime is bundled, so the
;     target machine does NOT need the .NET 9 Desktop Runtime installed.
;   * WPF single-file leaves a few native DLLs next to the .exe; we ship the WHOLE
;     publish folder (the app fails to start with only the .exe).
;   * Current-user-only install (no admin / no UAC). Installs under the user's profile
;     (%LOCALAPPDATA%\Programs\OopsType by default) — NOT C:\Program Files, which is
;     admin-only and would make it an all-users install.
;   * The app writes only to %LOCALAPPDATA%\OopsType and HKCU at runtime, so nothing it
;     does at runtime needs elevation.
;   * Autostart is owned by the app itself (General → Launch on Windows startup, an
;     HKCU\...\Run value). The installer does NOT create or delete that value at install
;     time; it only cleans it up on uninstall so no stale entry is left pointing at a
;     deleted exe. (The app sets autostart only once on first launch and never re-asserts
;     it, so deleting it on upgrade would silently disable it.)
; ─────────────────────────────────────────────────────────────────────────────

#define MyAppName        "OopsType"
#define MyAppVersion     "1.0.0"
#define MyAppPublisher   "ori halevi"
#define MyAppURL         "https://github.com/ori-halevi/OopsType"
#define MyAppExeName     "OopsType.exe"

; Stable app identity — never change this between versions, or upgrades won't be
; recognized and you'll get a second entry in Add/Remove Programs. The leading "{{"
; is Inno's escape for a literal "{" so the stored AppId becomes {GUID}.
#define MyAppId          "{{B9F3C2A7-1D4E-4A8B-9C6F-2E7A5D3B8F10}"

#define SrcRoot          "C:\Users\OH\Desktop\MyScripts\C Sharp\OopsType"
; Version-coupled so a version bump only needs editing MyAppVersion above. Resolves to
; ...\publish\OopsType 1.0.0 — must match your publish output folder.
#define PublishDir       SrcRoot + "\bin\Release\net9.0-windows\publish\" + MyAppName + " " + MyAppVersion

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Default install location. This is a per-user install (PrivilegesRequired=lowest below),
; so {autopf} resolves to %LOCALAPPDATA%\Programs — i.e. C:\Users\<user>\AppData\Local\
; Programs\OopsType. It deliberately does NOT default to C:\Program Files: that folder is
; admin-only and writing to it would be an all-users install, not "current user only".
; The user can still change the folder on the directory page (shown below).
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Always show the "choose install folder" page (you asked for this explicitly).
DisableDirPage=no

; Current-user-only install — no admin elevation, no UAC prompt. The whole install lands
; under the user's profile and Add/Remove Programs lists it for this user only.
PrivilegesRequired=lowest

; This is a 64-bit build — refuse to install on 32-bit Windows and use the real
; (non-redirected) 64-bit Program Files / registry view. x64compatible also allows
; installing on ARM64 (running under x64 emulation).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; If OopsType is running during install/uninstall, prompt the user to close it first
; (otherwise its files are locked). This is the exact single-instance mutex the app
; creates in App.OnStartup — the doubled "{{" escapes the brace for Inno.
AppMutex=Local\OopsType.SingleInstance.{{8F2C7A14-3E5B-4E2A-9D6F-7B1C0A4F8E92}

LicenseFile={#SrcRoot}\LICENSE
SetupIconFile={#SrcRoot}\Assets\logo.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}

WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes

OutputDir={#SrcRoot}\Installer\Output
OutputBaseFilename={#MyAppName}-{#MyAppVersion}-Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
; Bundle the Hebrew wizard UI too (matches the app's RTL support). If you don't have
; this file, comment the next line out — it ships with Inno Setup's Languages folder.
Name: "hebrew";  MessagesFile: "compiler:Languages\Hebrew.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Ship the entire publish folder: OopsType.exe, the WPF native DLLs, and Languages\.
; ignoreversion is correct here because the single-file exe has no meaningful file
; version to compare against on upgrade. The .pdb is debug symbols — not needed at runtime.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion; Excludes: "*.pdb"

[Icons]
Name: "{group}\{#MyAppName}";          Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";    Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Don't create or delete this at install time — the app owns its autostart Run value, and
; it only sets it once on first launch (deliberately never re-asserting it afterwards).
; Deleting it on upgrade would silently disable autostart for users who enabled it. We only
; remove the value on UNINSTALL, so no stale entry is left pointing at a deleted exe.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: none; ValueName: "OopsType"; \
    Flags: dontcreatekey uninsdeletevalue

[Run]
; Offer to launch OopsType when the installer finishes (skipped on /SILENT installs).
; The installer already runs unelevated as the current user, so the app launches in the
; right user context and its first-launch autostart lands in the correct HKCU.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall skipifsilent

[Code]
{ On uninstall, the app's data folder (%LOCALAPPDATA%\OopsType — settings.json, logs, and
  any user-added language packs) is left behind by default so a reinstall keeps the user's
  configuration. Here we ASK whether to wipe it for a full, clean removal. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\OopsType');
    if DirExists(DataDir) then
    begin
      if MsgBox('Also delete OopsType settings and data?' + #13#10 + #13#10 +
                DataDir + #13#10 + #13#10 +
                'Choose Yes for a full removal (settings, logs and custom languages will be lost).' + #13#10 +
                'Choose No to keep your configuration for a future reinstall.',
                mbConfirmation, MB_YESNO) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
