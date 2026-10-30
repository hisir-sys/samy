#define AppName "SAMY"
#define AppVersion "0.3.0"
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
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#AppExeName}
SetupLogging=yes
SetupIconFile=..\src\Samy\Assets\SamyLogo.ico

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Launch SAMY when I sign in"; GroupDescription: "Windows startup:"; Flags: unchecked

[Files]
Source: "..\dependencies\VC_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\SAMY"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; AppUserModelID: "SAMY.Desktop"
Name: "{autodesktop}\SAMY"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{userstartup}\SAMY"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: startup

[Code]

function IsVCRuntimeInstalled(): Boolean;
var
  Installed: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64,
    'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64',
    'Installed', Installed) and (Installed = 1);
end;

[Run]
Filename: "{tmp}\VC_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing Microsoft Visual C++ runtime..."; Flags: runhidden waituntilterminated; Check: not IsVCRuntimeInstalled
Filename: "{app}\{#AppExeName}"; Description: "Launch SAMY"; Flags: postinstall nowait skipifsilent
