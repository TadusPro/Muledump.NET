; Inno Setup script for Muledump.NET
#define MyAppName "Muledump.NET"
#define MyAppExe  "Muledump.NET.exe"
; CI passes /DMyAppVersion=...; default here for local builds
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

[Setup]
; ensure x64 install location
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Files]
; Package everything produced by dotnet publish
Source: "..\..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion replacesameversion

[Icons]
Name: "{group}\Muledump.NET"; Filename: "{app}\Muledump.NET.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Muledump.NET"; Filename: "{app}\Muledump.NET.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Run]
Filename: "{app}\Muledump.NET.exe"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
