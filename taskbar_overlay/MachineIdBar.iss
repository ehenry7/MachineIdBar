; MachineIdBar.iss — Inno Setup script
; Builds a single-file installer: MachineIdBar-Setup.exe
;
; Build:  "C:\Users\h00569653\AppData\Local\Programs\Inno Setup 6\ISCC.exe" MachineIdBar.iss
; Output: dist\MachineIdBar-Setup.exe

#define MyAppName "Machine ID Bar"
#define MyAppExeName "MachineIdBar.exe"
#define MyAppVersion "1.0"

[Setup]
AppId={{B8F3A2E1-7C4D-4A9E-B6F1-3E5A7C9D2B8E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=MachineIdBar
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={localappdata}\MachineIdBar
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=MachineIdBar-Setup
Compression=lzma2
SolidCompression=yes
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Launch on Windows startup"; GroupDescription: "Options:"

[Files]
Source: "MachineIdBar.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Kill running instance before uninstall
Filename: "{cmd}"; Parameters: "/c taskkill /F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[UninstallDelete]
Type: files; Name: "{app}\{#MyAppExeName}"
Type: dirifempty; Name: "{app}"
