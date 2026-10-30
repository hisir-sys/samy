#define AppName "SAMY"
#define AppVersion "0.2.0"
#define AppPublisher "SAMY"
#define AppExeName "Samy.exe"

[Setup]
AppId={{A1C5E3F2-7D9B-4A61-8F3C-5B2D6E9A1047}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\SAMY
DefaultGroupName=SAMY
DisableProgramGroupPage=yes
OutputDir=..\artifacts
OutputBaseFilename=SAMY-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#AppExeName}
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Launch SAMY when I sign in"; GroupDescription: "Windows startup:"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\SAMY"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\SAMY"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\SAMY"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: startup

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch SAMY"; Flags: postinstall nowait skipifsilent
