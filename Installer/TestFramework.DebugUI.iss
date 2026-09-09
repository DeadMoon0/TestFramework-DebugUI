; Installer for the DebugUI launcher.
;
; What this installs is the launcher, and only the launcher. The application itself is fetched by the
; launcher into %USERPROFILE%\.testframework\versions on first run, which is why this file is small
; and why it almost never has to change: a new DebugUI release needs no new installer.
;
; Per-user, deliberately. PrivilegesRequired=lowest means the whole install runs without a UAC prompt
; and without administrator rights, which is how VS Code's user installer and Discord both work. The
; one exception is the .NET desktop runtime, which is machine-wide and elevates itself; see
; PrepareToInstall below.
;
; Not signed. A self-signed Authenticode signature does nothing for SmartScreen, so this ships
; unsigned and users see "Windows protected your PC" with a More info -> Run anyway path. That is the
; accepted cost of having no certificate; nothing in the installer can improve it.
;
; Program and data are kept apart on purpose:
;
;   {localappdata}\Programs\TestFramework   the launcher. Installed, upgraded and removed by this.
;   %USERPROFILE%\.testframework            versions, run journals, settings, themes. Not ours.
;
; So uninstalling can take the program and the downloaded version cache while leaving somebody's
; settings, custom themes and recorded runs alone.

#ifndef AppVersion
  ; Only so the script can be opened and compiled by hand. The workflow always passes the real one.
  #define AppVersion "0.0.0"
#endif

#define AppName "TestFramework Debugger"
#define LauncherExe "TestFramework.DebugUI.Launcher.exe"
#define DataFolder ".testframework"

[Setup]
; Never change this. It is what Windows matches an upgrade and an uninstall against; a new value
; turns every future release into a second, parallel installation.
AppId={{C297FC20-64B2-45DF-8654-83AA7CCE99EB}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=DeadMoon0
AppPublisherURL=https://github.com/DeadMoon0/TestFramework-DebugUI
AppSupportURL=https://github.com/DeadMoon0/TestFramework-DebugUI/issues
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#LauncherExe}

; No admin, no UAC, and no way to ask for one. An all-users install is not merely unnecessary here,
; it would break the launcher: it lands in Program Files, where the launcher cannot replace its own
; executable, and self-update is the thing that stops a launcher fix needing a new installer.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\TestFramework
DisableDirPage=yes

; The Start menu folder is not worth a page of its own for a single shortcut.
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

OutputDir=.
OutputBaseFilename=TestFramework.DebugUI.Setup
SetupIconFile=..\TestFramework.DebugUI.Launcher\Icon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; x64compatible rather than x64os, so an ARM64 machine is not turned away — .NET runs natively there
; and the launcher is AnyCPU. The one soft spot is the runtime check below, which looks in the native
; shared folder: an ARM64 machine that only has the x64 runtime under emulation keeps it elsewhere, so
; the check would offer a runtime that is arguably already usable. Offering a second one is a nuisance;
; missing a genuinely absent one would be a broken first run, so it errs this way on purpose.
ArchitecturesAllowed=x64compatible

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
; Everything dotnet publish produced for the launcher. The workflow lays it out beside this script.
Source: "launcher\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Both shortcuts point at the launcher and never at a version folder. That is the whole trick behind
; a shortcut that survives updates: the launcher resolves the newest installed version at start-up,
; so the path in the shortcut never has to change. Discord's shortcut does exactly this.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#LauncherExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#LauncherExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#LauncherExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; Note what is NOT here: the run-journal folder is not created by the installer. Core gates journalling
; on that folder existing, and creating it is the launcher's first job on every start — including the
; starts where the network is down and the update check is skipped. An installer that created it would
; arm journalling on a machine whose launcher had never successfully run.

[Code]
const
  RuntimeUrl = 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe';

var
  DownloadPage: TDownloadWizardPage;

{ Whether a .NET 8 desktop runtime is already here. Enumerated from disk rather than read from the
  registry because the shared folder is what the host actually loads from, and a registry entry left
  behind by a removed runtime would say yes when nothing can run. }
function DesktopRuntimePresent: Boolean;
var
  Find: TFindRec;
  Root: string;
begin
  Result := False;

  if not IsWin64 then
    Exit;

  Root := ExpandConstant('{commonpf64}') + '\dotnet\shared\Microsoft.WindowsDesktop.App';

  if not DirExists(Root) then
    Exit;

  if FindFirst(Root + '\8.*', Find) then
  try
    repeat
      if (Find.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        Result := True;
        Exit;
      end;
    until not FindNext(Find);
  finally
    FindClose(Find);
  end;
end;

function OnDownloadProgress(const Url, Filename: string; const Progress, ProgressMax: Int64): Boolean;
begin
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), '', @OnDownloadProgress);
end;

{ The runtime is the one part of this install that cannot be done per-user: the .NET desktop runtime
  is machine-wide, so its own installer elevates and prompts. Offered rather than forced, and a
  refusal does not fail the install — the launcher is still worth having on disk, and someone who
  says no here can install the runtime later without running this again. }
function PrepareToInstall(var NeedsRestart: Boolean): string;
var
  Installer: string;
  ExitCode: Integer;
begin
  Result := '';

  if DesktopRuntimePresent then
    Exit;

  if MsgBox('The debugger needs the .NET 8 Desktop Runtime, which is not installed on this machine.'
    + #13#10#13#10 + 'Download and install it now? Windows will ask for administrator rights, because'
    + ' the runtime is shared by every application that uses it.',
    mbConfirmation, MB_YESNO) <> IDYES then
  begin
    MsgBox('Skipped. The debugger will not start until the .NET 8 Desktop Runtime is installed:'
      + #13#10#13#10 + RuntimeUrl, mbInformation, MB_OK);
    Exit;
  end;

  DownloadPage.Clear;

  { No expected hash, and it cannot be one: aka.ms resolves to whatever the current 8.0 patch is, so a
    pinned digest would fail on the next runtime update instead of catching anything. What is checked
    is the source — HTTPS to a Microsoft domain — and the file is only ever handed to Windows to run,
    never trusted for anything else. }
  DownloadPage.Add(RuntimeUrl, 'windowsdesktop-runtime.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
    except
      MsgBox('The runtime could not be downloaded:' + #13#10#13#10 + GetExceptionMessage
        + #13#10#13#10 + 'Install it by hand from:' + #13#10 + RuntimeUrl, mbError, MB_OK);
      Exit;
    end;
  finally
    DownloadPage.Hide;
  end;

  Installer := ExpandConstant('{tmp}\windowsdesktop-runtime.exe');

  { /passive rather than /quiet: it has to be able to show its own elevation prompt and progress,
    and a silent installer that is waiting on an invisible UAC dialog looks like a hang. }
  if not Exec(Installer, '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ExitCode) then
    MsgBox('The runtime installer could not be started. Install it by hand from:'
      + #13#10 + RuntimeUrl, mbError, MB_OK)
  else if ExitCode = 3010 then
    NeedsRestart := True
  else if (ExitCode <> 0) and (ExitCode <> 1638) then
    MsgBox('The runtime installer reported code ' + IntToStr(ExitCode) + '. If the debugger does not'
      + ' start, install the runtime by hand from:' + #13#10 + RuntimeUrl, mbError, MB_OK);
end;

{ Takes the downloaded version cache with it, because that is exactly what it is: copies of the
  application the launcher can fetch again, and hundreds of megabytes of them. Leaves the run
  journals, settings and hand-written themes, which nobody can fetch again. }
procedure CurUninstallStepChanged(CurStep: TUninstallStep);
var
  Data: string;
begin
  if CurStep <> usPostUninstall then
    Exit;

  Data := ExpandConstant('{%USERPROFILE}') + '\{#DataFolder}';

  DelTree(Data + '\versions', True, True, True);
  DelTree(Data + '\staging', True, True, True);
end;
