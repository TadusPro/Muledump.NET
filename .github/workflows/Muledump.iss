; .github/workflows/Muledump.iss
#define MyAppName "Muledump.NET"
#define MyAppExe  "Muledump.NET.exe"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#ifndef InstallerHash
  #define InstallerHash ""
#endif

[Setup]
AppId={{8E2E4B6C-2A2C-4F7A-9C2F-7A1A7F1A3F11}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Muledump
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\..\installer
OutputBaseFilename=Muledump.NET-setup-windows-x64
Compression=lzma
SolidCompression=yes
UsePreviousAppDir=yes
DirExistsWarning=no
CloseApplications=yes
RestartApplications=yes
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
SetupLogging=yes

[Files]
Source: "..\..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion replacesameversion
Source: "..\..\publish\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall

[Icons]
Name: "{group}\{#MyAppName}";      Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing WebView2 runtime..."; Check: not WebView2Present
Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{code:GetInstalledDataDir}"; Check: ShouldRemoveData

[Registry]
Root: HKCU; Subkey: "Software\{#MyAppName}"; ValueType: string; ValueName: "DataDir"; ValueData: "{code:GetChosenDataDir}"; Flags: uninsdeletekeyifempty

[Code]
var
  DataDirPage: TInputDirWizardPage;
  RemoveUserData: Boolean;

function GetChosenDataDir(Param: string): string;
begin
  if Assigned(DataDirPage) then
    Result := DataDirPage.Values[0]
  else
    Result := ExpandConstant('{userappdata}\{#MyAppName}');
end;

function GetInstalledDataDir(Param: string): string;
var
  DataDir: string;
begin
  if RegQueryStringValue(HKCU, 'Software\' + '{#MyAppName}', 'DataDir', DataDir) then
    Result := DataDir
  else
    Result := ExpandConstant('{userappdata}\{#MyAppName}');
end;

function WebView2Present: Boolean;
begin
  Result :=
    DirExists(ExpandConstant('{pf32}\Microsoft\EdgeWebView\Application')) or
    DirExists(ExpandConstant('{localappdata}\Microsoft\EdgeWebView\Application'));
end;

function ShouldRemoveData: Boolean;
begin
  Result := RemoveUserData;
end;

procedure SaveInstallerHash;
var
  Hash, Pth: String;
begin
  Hash := '{#InstallerHash}';
  if Trim(Hash) <> '' then
  begin
    Pth := ExpandConstant(GetChosenDataDir('') + '\Updates\last_installer.sha256');
    ForceDirectories(ExtractFilePath(Pth));
    SaveStringToFile(Pth, Hash, False);
  end;
end;

procedure InitializeWizard;
var
  PrevData: String;
begin
  DataDirPage := CreateInputDirPage(
    wpSelectDir,
    'Choose Data Location',
    'Where should Muledump.NET store its data?',
    'Choose a writable folder. If unsure, keep the default.',
    False, 'Muledump.NET');
  DataDirPage.Add('Data folder:');

  if RegQueryStringValue(HKCU, 'Software\' + '{#MyAppName}', 'DataDir', PrevData) and (PrevData <> '') then
    DataDirPage.Values[0] := PrevData
  else
    DataDirPage.Values[0] := ExpandConstant('{userappdata}\{#MyAppName}');
end;

function InitializeUninstall(): Boolean;
var
  R: Integer;
begin
  // Ask once at uninstall start; store choice for [UninstallDelete] check
  R := MsgBox('Do you also want to remove all user data and settings?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2);
  RemoveUserData := (R = IDYES);
  Result := True; // continue with uninstall
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  D: string;
begin
  if CurStep = ssInstall then
  begin
    D := GetChosenDataDir('');
    if D = '' then
      D := ExpandConstant('{userappdata}\{#MyAppName}');
    ForceDirectories(D);
    SaveInstallerHash();
  end;
end;
