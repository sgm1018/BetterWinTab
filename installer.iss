; ============================================================
;  BetterWinTab — Inno Setup Installer Script
;  Requires Inno Setup 6.x  (https://jrsoftware.org/isinfo.php)
;
;  How to build:
;    1. Download the WebView2 Evergreen Bootstrapper (one-time, ~1.6 MB) and place it at:
;         redist\MicrosoftEdgeWebview2Setup.exe
;       Download from: https://developer.microsoft.com/microsoft-edge/webview2/
;    2. Publish the x64 project:
;         dotnet publish BetterWinTab.csproj -p:Platform=x64 -c Release
;    3. Open this file in Inno Setup Compiler (or run iscc.exe):
;         iscc installer.iss
;       To inject a specific version:
;         iscc /DAppVersion=1.2.0 installer.iss
;  The resulting Setup.exe will appear in the installer-output\ folder.
; ============================================================

#define AppName      "BetterWinTab"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppPublisher "sergio.gonzalez"
#define AppURL       "https://github.com/sgm1018/BetterWinTab"
#define AppExeName   "BetterWinTab.exe"
#define SourceDir    "bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"

[Setup]
; --- Identity ---
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}

; --- Install location ---
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes

; --- Output ---
OutputDir=installer-output
OutputBaseFilename=BetterWinTab-Setup-{#AppVersion}-x64
SetupIconFile=Assets\logos\BetterWinTab.ico

; --- Compression ---
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible

; --- UI ---
WizardStyle=modern
DisableProgramGroupPage=yes
; Branding images — generated from Assets\logos\betterWinTab.png
WizardImageFile=Assets\installer\wizard-banner.png
WizardSmallImageFile=Assets\installer\wizard-small.png
WizardImageStretch=yes
WizardImageBackColor=$000000

; --- Privileges ---
; Use "lowest" so the app installs per-user without UAC prompt.
; Change to "admin" if you want a system-wide install.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; --- Uninstall ---
UninstallDisplayIcon={app}\{#AppName}.ico
UninstallDisplayName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";   Description: "{cm:CreateDesktopIcon}";   GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupentry";  Description: "Launch {#AppName} on Windows startup"; GroupDescription: "Startup options:"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*";                    DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Application icon — deployed explicitly so shortcuts and "Apps & features" always show the correct icon
Source: "Assets\logos\BetterWinTab.ico";        DestDir: "{app}"; Flags: ignoreversion
; WebView2 Evergreen Bootstrapper — copied to {tmp} and deleted after install
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\{#AppName}";           Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppName}.ico"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppName}";   Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppName}.ico"; Tasks: desktopicon

[Registry]
; Add startup entry when the user selected the task
Root: HKCU; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#AppName}"; \
  ValueData: """{app}\{#AppExeName}"""; \
  Flags: uninsdeletevalue; \
  Tasks: startupentry

[Run]
; Install WebView2 Runtime silently if not already present on this machine
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; \
  StatusMsg: "Installing WebView2 Runtime (required)..."; \
  Check: WebView2NotInstalled; Flags: waituntilterminated

Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
; Remove from startup registry on uninstall (safety net in addition to Flags: uninsdeletevalue)
Filename: "reg.exe"; \
  Parameters: "delete ""HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"" /v ""{#AppName}"" /f"; \
  Flags: runhidden; RunOnceId: "RemoveStartup"

[UninstallDelete]
; Remove all user settings and onboarding state so a fresh install starts from scratch
Type: filesandordirs; Name: "{userappdata}\{#AppName}"

[Code]
// Returns True if WebView2 Runtime is NOT installed — used by the [Run] check above.
// Checks the registry key that the WebView2 Runtime registers on install.
function WebView2NotInstalled: Boolean;
var
  sVersion: String;
begin
  Result := not RegQueryStringValue(
    HKLM,
    'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'pv',
    sVersion
  );
end;

// Optionally show a reminder if the user chose not to add a startup entry
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not IsTaskSelected('startupentry') then
      MsgBox('{#AppName} will not start automatically with Windows.' + #13#10 +
             'You can enable this at any time from Settings → General.', mbInformation, MB_OK);
  end;
end;
