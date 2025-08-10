; .github/workflows/Muledump.iss
#define MyAppName "Muledump.NET"
#define MyAppExe  "Muledump.NET.exe"
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Muledump
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\..\installer
OutputBaseFilename={#MyAppName}-Setup
Compression=lzma
SolidCompression=yes

[Files]
; publish/ is two levels up from this .iss location
Source: "..\..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion replacesameversion

[Icons]
Name: "{group}\{#MyAppName}";      Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
