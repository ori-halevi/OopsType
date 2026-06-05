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
;   * All-users install (requires admin / one UAC prompt). Installs the binaries under
;     C:\Program Files\OopsType ({autopf}) so every user on the machine shares one copy.
;   * The app itself never needs elevation at runtime: it writes only to the CURRENT user's
;     %LOCALAPPDATA%\OopsType (settings.json, logs, custom languages) and HKCU. So settings,
;     autostart and the chosen UI language are all PER-USER even though the program is shared.
;   * Setup hands the user's chosen wizard language to the app: on ssPostInstall we drop a
;     one-line setup.lang next to the exe ("he"/"en"); each user's first launch reads it and
;     adopts it as their initial UI language (see SettingsService.ReadSetupLanguage).
;   * Autostart is owned by the app itself (General → Launch on Windows startup, an
;     HKCU\...\Run value). The installer does NOT create or delete that value at install
;     time; it only cleans it up on uninstall so no stale entry is left pointing at a
;     deleted exe. (The app sets autostart only once on first launch and never re-asserts
;     it, so deleting it on upgrade would silently disable it.)
; ─────────────────────────────────────────────────────────────────────────────

#define MyAppName        "OopsType"
#define MyAppVersion     "2.1.0"
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

; Default install location. This is an all-users install (PrivilegesRequired=admin below),
; so {autopf} resolves to C:\Program Files\OopsType — one shared copy for everyone on the
; machine. The user can still change the folder on the directory page (shown below).
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Always show the "choose install folder" page (you asked for this explicitly).
DisableDirPage=no

; All-users install — requires admin elevation (one UAC prompt). Files land in Program Files
; and Add/Remove Programs lists OopsType machine-wide for every user.
PrivilegesRequired=admin

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
; NOTE: autostart is per-user (HKCU). On an all-users uninstall this only clears the Run value
; of the user running the uninstaller; another user's stale value, if any, simply points at a
; missing exe and Windows silently ignores it at logon — harmless.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: none; ValueName: "OopsType"; \
    Flags: dontcreatekey uninsdeletevalue

[UninstallDelete]
; setup.lang is written by [Code] at ssPostInstall (not part of [Files]), so remove it
; explicitly on uninstall — otherwise it would keep the {app} folder from being deleted.
Type: files; Name: "{app}\setup.lang"

[Run]
; Offer to launch OopsType when the installer finishes (skipped on /SILENT installs).
; The installer is elevated (admin), so without runasoriginaluser the app would start AS the
; elevating admin and its first-launch per-user state (settings.json, autostart HKCU, the
; setup.lang language read) would land in the wrong profile. runasoriginaluser drops back to
; the originally logged-on user so that state is created in the right place.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  ProfileListKey = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList';

{ Map the wizard language (the [Languages] entry name the user picked) to the app's two-letter
  language-pack code. Extend the case as you add more bundled wizard languages. }
function AppLanguageCode(): String;
begin
  if ActiveLanguage = 'hebrew' then
    Result := 'he'
  else
    Result := 'en';
end;

{ Hand the user's chosen wizard language to the app. We drop a one-line setup.lang next to the
  exe; the app reads it on each user's FIRST launch and adopts it as their initial UI language
  (SettingsService.ReadSetupLanguage). Written here rather than via [Files] because the content
  is dynamic; removed again by [UninstallDelete]. }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\setup.lang'), AppLanguageCode(), False);
end;

{ This is an all-users install but the app's data is PER-USER (%LOCALAPPDATA%\OopsType —
  settings.json, logs, custom language packs). A "full removal" must therefore wipe that folder
  in EVERY user profile, not just the one running the uninstaller. The uninstaller is elevated,
  so we enumerate the machine's profiles via HKLM\...\ProfileList and delete each one's copy.
  Only profiles that actually ran OopsType have the folder, so this never touches unrelated data. }
function DataDirForProfile(const ProfilePath: String): String;
begin
  Result := AddBackslash(ProfilePath) + 'AppData\Local\OopsType';
end;

function AnyUserDataExists(): Boolean;
var
  Names: TArrayOfString;
  i: Integer;
  ProfilePath: String;
begin
  Result := False;
  if not RegGetSubkeyNames(HKEY_LOCAL_MACHINE, ProfileListKey, Names) then
    Exit;
  for i := 0 to GetArrayLength(Names) - 1 do
    if RegQueryStringValue(HKEY_LOCAL_MACHINE, ProfileListKey + '\' + Names[i],
                           'ProfileImagePath', ProfilePath) then
      if DirExists(DataDirForProfile(ProfilePath)) then
      begin
        Result := True;
        Exit;
      end;
end;

procedure DeleteAllUsersData();
var
  Names: TArrayOfString;
  i: Integer;
  ProfilePath, DataDir: String;
begin
  if not RegGetSubkeyNames(HKEY_LOCAL_MACHINE, ProfileListKey, Names) then
    Exit;
  for i := 0 to GetArrayLength(Names) - 1 do
    if RegQueryStringValue(HKEY_LOCAL_MACHINE, ProfileListKey + '\' + Names[i],
                           'ProfileImagePath', ProfilePath) then
    begin
      DataDir := DataDirForProfile(ProfilePath);
      if DirExists(DataDir) then
        DelTree(DataDir, True, True, True);
    end;
end;

{ On uninstall, the app's per-user data is left behind by default so a reinstall keeps the
  user's configuration. Here we ASK whether to wipe it (for all users) for a full, clean
  removal. MB_DEFBUTTON2 makes "No" (keep) the default so a /VERYSILENT uninstall preserves
  data unless explicitly told otherwise. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if AnyUserDataExists() then
    begin
      if MsgBox('Also delete OopsType settings and data for all users?' + #13#10 + #13#10 +
                'Choose Yes for a full removal (settings, logs and custom languages will be lost).' + #13#10 +
                'Choose No to keep your configuration for a future reinstall.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DeleteAllUsersData();
    end;
  end;
end;
