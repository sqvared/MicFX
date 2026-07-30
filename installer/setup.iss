; MicFX installer
; ------------------------------------------------------------------
; Builds a single Setup.exe that:
;   1) Installs the MicFX app (self-contained published exe) to Program Files
;   2) Creates Start Menu / Desktop shortcuts and an uninstall entry
;   3) Optionally fetches the OFFICIAL VB-Audio Virtual Cable installer
;      directly from vb-audio.com at install time and launches it, rather
;      than bundling/redistributing their binary ourselves (VB-Audio asks
;      that integration/distribution be arranged with them directly - see
;      https://vb-audio.com/Services/licensing.htm - downloading fresh from
;      their own site at install time respects that).
;
; Requires: Inno Setup 6 (https://jrsoftware.org/isinfo.php) to compile.
; Build steps are in ../README.md under "Building the installer".
; ------------------------------------------------------------------

#define MyAppName "MicFX"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "MicFX"
#define MyAppExeName "MicFXApp.exe"

; Path to the self-contained published output, relative to this script.
; Produced by: dotnet publish -c Release -r win-x64 --self-contained true
;              -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
#define PublishDir "..\MicFXApp\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{6F3B2E60-6B7B-4E3B-9B4C-9A6F0C9E7A11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=MicFX-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "installcable"; Description: "Install VB-Audio Virtual Cable (downloads the official installer from vb-audio.com)"; GroupDescription: "Virtual microphone driver:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; If your publish output produces additional files (it usually won't with
; PublishSingleFile), uncomment the line below to grab everything else too:
; Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Launch MicFX after install (optional, unchecked by default so first run is deliberate)
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
const
  CableDownloadUrl = 'https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip';

var
  CableZipPath, CableExtractDir, CableSetupExe: string;

function IsCableAlreadyInstalled(): Boolean;
var
  names: TArrayOfString;
  i: Integer;
  displayName: string;
begin
  Result := False;
  // Heuristic: scan uninstall registry entries for anything mentioning VB-CABLE / VB-Audio.
  if RegGetSubkeyNames(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', names) then
  begin
    for i := 0 to GetArrayLength(names) - 1 do
    begin
      if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + names[i], 'DisplayName', displayName) then
      begin
        if (Pos('VB-CABLE', Uppercase(displayName)) > 0) or (Pos('VB-AUDIO', Uppercase(displayName)) > 0) then
        begin
          Result := True;
          exit;
        end;
      end;
    end;
  end;
end;

// Downloads a file using PowerShell's Invoke-WebRequest so we don't need any
// extra Inno plugins. Returns True on success.
function DownloadViaPowerShell(const Url, DestPath: string): Boolean;
var
  ResultCode: Integer;
  Cmd, Args: string;
begin
  Args := '-Uri ''' + Url + ''' -OutFile ''' + DestPath + ''' -UseBasicParsing';
  Cmd := '-NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference=''SilentlyContinue''; Invoke-WebRequest ' + Args + '"';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
            and (ResultCode = 0) and FileExists(DestPath);
end;

function ExtractZipViaPowerShell(const ZipPath, DestDir: string): Boolean;
var
  ResultCode: Integer;
  Cmd, Args: string;
begin
  Args := '-LiteralPath ''' + ZipPath + ''' -DestinationPath ''' + DestDir + ''' -Force';
  Cmd := '-NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive ' + Args + '"';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
            and (ResultCode = 0);
end;

procedure InstallVBCable();
var
  found: TFindRec;
  ResultCode: Integer;
begin
  if IsCableAlreadyInstalled() then
  begin
    MsgBox('VB-Audio Virtual Cable already appears to be installed - skipping.', mbInformation, MB_OK);
    exit;
  end;

  CableZipPath := ExpandConstant('{tmp}\VBCABLE_Driver_Pack45.zip');
  CableExtractDir := ExpandConstant('{tmp}\VBCABLE_Extract');

  if not DownloadViaPowerShell(CableDownloadUrl, CableZipPath) then
  begin
    MsgBox('Could not automatically download VB-Audio Virtual Cable (no internet access, or vb-audio.com is unreachable right now).' + #13#10 +
           'You can install it manually afterwards from https://vb-audio.com/Cable/', mbInformation, MB_OK);
    exit;
  end;

  CreateDir(CableExtractDir);
  if not ExtractZipViaPowerShell(CableZipPath, CableExtractDir) then
  begin
    MsgBox('Downloaded VB-Audio Virtual Cable but could not unpack it automatically.' + #13#10 +
           'Please install it manually from https://vb-audio.com/Cable/', mbInformation, MB_OK);
    exit;
  end;

  // Locate the setup exe inside the extracted folder (filename has changed
  // across VB-Audio's driver pack revisions, so search rather than hardcode).
  CableSetupExe := '';
  if FindFirst(CableExtractDir + '\*Setup*x64*.exe', found) then
  begin
    try
      CableSetupExe := CableExtractDir + '\' + found.Name;
    finally
      FindClose(found);
    end;
  end;

  if CableSetupExe = '' then
  begin
    MsgBox('Downloaded VB-Audio Virtual Cable but could not find its setup program automatically.' + #13#10 +
           'Please open ' + CableExtractDir + ' and run the setup exe manually, or install from https://vb-audio.com/Cable/',
           mbInformation, MB_OK);
    exit;
  end;

  MsgBox('The official VB-Audio Virtual Cable installer will now open.' + #13#10 +
         'Please follow its prompts, and REBOOT when it tells you to - the driver ' +
         'will not appear as a device until you do.', mbInformation, MB_OK);

  if not Exec(CableSetupExe, '', CableExtractDir, SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
    MsgBox('Could not launch the VB-Audio Virtual Cable installer. Please install it manually from https://vb-audio.com/Cable/', mbInformation, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('installcable') then
    InstallVBCable();
end;
