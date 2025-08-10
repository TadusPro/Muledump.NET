; .github/workflows/Muledump.iss
#define MyAppName "Muledump.NET"
#define MyAppExe  "Muledump.NET.exe"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

[Setup]
AppId={{8E2E4B6C-2A2C-4F7A-9C2F-7A1A7F1A3F11}
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
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}

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

[Code]
var
  DataDirPage: TInputDirWizardPage;

function WebView2Present: Boolean;
begin
  Result :=
    DirExists(ExpandConstant('{pf32}\Microsoft\EdgeWebView\Application')) or
    DirExists(ExpandConstant('{localappdata}\Microsoft\EdgeWebView\Application'));
end;

function GetChosenDataDir(Param: string): string;
begin
  if Assigned(DataDirPage) then
    Result := DataDirPage.Values[0]
  else
    Result := ExpandConstant('{userappdata}\{#MyAppName}');
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

  ; Prefill with previous choice if present
  if RegQueryStringValue(HKCU, 'Software\' + '{#MyAppName}', 'DataDir', PrevData) and (PrevData <> '') then
    DataDirPage.Values[0] := PrevData
  else
    DataDirPage.Values[0] := ExpandConstant('{userappdata}\{#MyAppName}');
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
  end;
end;

[Registry]
Root: HKCU; Subkey: "Software\{#MyAppName}"; ValueType: string; ValueName: "DataDir"; ValueData: "{code:GetChosenDataDir}"; Flags: uninsdeletekeyifempty
