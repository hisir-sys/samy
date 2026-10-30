# SAMY Windows installer

The main-branch Windows workflow publishes **SAMY-Setup.exe** as a GitHub Release asset after a successful x64 installer build.

## Download

Open the [latest SAMY release](https://github.com/hisir-sys/samy/releases/latest) and download **SAMY-Setup.exe**.

If a workflow is still running and no release has been published yet, open the repository's Actions tab, select the latest successful **Windows build and installer** run, and download the **SAMY-Windows-Installer** artifact. Actions artifacts expire; GitHub Release assets are the permanent download.

## What the installer does

- Installs SAMY for the the selected Windows installation scope without requiring an administrator prompt.
- Adds a SAMY shortcut to the Start Menu.
- Offers an optional Desktop shortcut.
- Offers optional launch-at-sign-in through the current user's Startup folder.
- Offers to launch SAMY after installation.
- Registers a normal Windows uninstall entry.

## Build locally

On Windows 11, install the .NET 10 SDK and Inno Setup 6, then run from the repository root:

```powershell
dotnet publish .\src\Samy\Samy.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -o publish\win-x64
New-Item -ItemType Directory -Force artifacts | Out-Null
& "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe" .\installer\SAMY.iss
```

The installer is written to `artifacts\SAMY-Setup.exe`.

## Platform and trust notes

- This release targets Windows 11 x64. ARM64 packaging is not included.
- The app uses self-contained Windows App SDK deployment; the installer bundles the published application files.
- The installer is not code-signed. Windows SmartScreen may show an unknown-publisher warning.
- A successful CI build verifies compilation and packaging, not a full interactive Windows 11 usability test. Test installation, launch, shortcuts, app behavior, and uninstall on a Windows 11 device before broad distribution.


## Runtime prerequisite and startup diagnostics

The installer bundles and installs the Microsoft Visual C++ 2015–2022 x64 runtime before launching SAMY. This is required by native Windows App SDK components on clean Windows installations. If SAMY still cannot start, the app writes startup exceptions and window-configuration fallback details to:

`%LOCALAPPDATA%\SAMY\startup-error.log`

The recovery window is intended to remain visible when SAMY's normal window construction fails. Please include this log when reporting a launch failure; do not delete the SAMY data folder while troubleshooting.
