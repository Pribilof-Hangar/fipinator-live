; FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
; https://github.com/Pribilof-Hangar/fipinator-live
; FIPinator Live installer (Inno Setup 6). Built by tools\build-release.ps1, which publishes the app into
; publish\release\FIPinatorLive first (with SimConnect.dll copied from the MSFS 2024 SDK; never in git).
; Per-user, no admin rights: installs into %LOCALAPPDATA%\Programs\FIPinator Live, like the developer
; install script, so it replaces that copy cleanly. The user's panels and gauges (Documents\FIPinator 3000)
; are never touched, by install or uninstall.

#ifndef AppVersion
  #define AppVersion "0.9.0"
#endif
#define AppName "FIPinator Live"
#define AppExe "FIPinatorLive.exe"

[Setup]
AppId={{6E2C9A41-5B7D-4E8F-9A30-F1D2C3B4A501}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Pribilof-Hangar
AppPublisherURL=https://github.com/Pribilof-Hangar/fipinator-live
AppSupportURL=https://github.com/Pribilof-Hangar/fipinator-live
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\publish\installers
OutputBaseFilename=FIPinator-Live-Setup-{#AppVersion}
SetupIconFile=..\assets\icon\fipinator.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
InfoBeforeFile=live-before.txt
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Put a FIPinator Live shortcut on the desktop"
; Ticked by default (owner, 2026-11-14): most people want Live's companion too; one download, no download from inside the app.
#define Setup3000 AddBackslash(SourcePath) + "..\publish\installers\FIPinator-3000-Setup-" + AppVersion + ".exe"
#if FileExists(Setup3000)
Name: "install3000"; Description: "Also install FIPinator 3000 (Live's companion, to change panels and gauges)"
#endif

[InstallDelete]
; The bundled gauges come with the program: clear the old copy so gauges dropped in a new version don't linger.
; (The user's own panels and gauges live in Documents\FIPinator 3000 and are never touched.)
Type: filesandordirs; Name: "{app}\Gauges"

[Files]
Source: "..\publish\release\FIPinatorLive\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; FIPinator 3000's own installer, run after Live's files are in (built first by tools\build-release.ps1).
#if FileExists(Setup3000)
Source: "..\publish\installers\FIPinator-3000-Setup-{#AppVersion}.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Tasks: install3000
#endif

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Runs your gauge panels on the Logitech FIPs"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Runs your gauge panels on the Logitech FIPs"; Tasks: desktopicon

[Run]
#if FileExists(Setup3000)
Filename: "{tmp}\FIPinator-3000-Setup-{#AppVersion}.exe"; Parameters: "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"; StatusMsg: "Installing FIPinator 3000..."; Flags: waituntilterminated; Tasks: install3000
#endif
Filename: "{app}\{#AppExe}"; Description: "Start FIPinator Live now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Only the program's own folder. Panels, gauges and settings in Documents\FIPinator 3000 stay.
Type: filesandordirs; Name: "{app}"

[Messages]
FinishedLabel=FIPinator Live is installed.%n%nStart it from the desktop or the Start menu. The first time, "Check my setup" shows whether the Logitech FIP driver, your FIPs and the sim are ready, and what to do next.%n%nYour panels and gauges are kept in Documents\FIPinator 3000. Uninstalling FIPinator Live never deletes them.

[Code]
const
  LogitechPage = 'https://support.logi.com/hc/en-us/articles/360025131294-Flight-Instrument-Panel-Drivers';

function InitializeSetup(): Boolean;
var
  Version: TWindowsVersion;
begin
  Result := True;
  GetWindowsVersionEx(Version);
  // Windows 11 is Windows 10.0 build 22000 or later.
  if (Version.Major < 10) or ((Version.Major = 10) and (Version.Build < 22000)) then
  begin
    MsgBox('FIPinator Live is made for Windows 11, and this PC runs an older Windows.' + #13#10 + #13#10 +
      'It has only been tested on Windows 11, so it won''t install here.', mbError, MB_OK);
    Result := False;
  end;
end;

function LogitechDriverFound(): Boolean;
var
  Path: String;
begin
  Result := (RegQueryStringValue(HKLM64, 'SOFTWARE\Logitech\DirectOutput', 'DirectOutput', Path) and FileExists(Path))
         or (RegQueryStringValue(HKLM64, 'SOFTWARE\Saitek\DirectOutput', 'DirectOutput', Path) and FileExists(Path));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
begin
  if (CurStep = ssPostInstall) and not WizardSilent() and not LogitechDriverFound() then
  begin
    if MsgBox('One more thing: the Logitech Flight Instrument Panel driver isn''t on this PC yet.' + #13#10 + #13#10 +
      'FIPinator Live needs it to draw on the FIPs. It''s free from Logitech.' + #13#10 + #13#10 +
      'Open Logitech''s download page now?', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', LogitechPage, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;
