; Inno Setup script for Muledump.NET
#define MyAppName "Muledump.NET"
#define MyAppExe  "Muledump.NET.exe"
; CI passes /DMyAppVersion=...; default here for local builds
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Muledump
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\..\installer
OutputBaseFilename={#MyAppName}-Setup
Compression=lzma
SolidCompression=yes

[Files]
; Package everything produced by dotnet publish
Source: "..\..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion replacesameversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Run]
Filename: "{app}\\{#MyAppExe}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
