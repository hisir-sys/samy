# SAMY Windows installer

The Windows CI workflow creates **SAMY-Setup.exe** for Windows 11 x64.

## What the installer does

- Installs SAMY for the current user without an administrator prompt.
- Adds a SAMY shortcut to the Start Menu.
- Offers an optional Desktop shortcut.
- Offers optional launch-at-sign-in through the current user's Startup folder.
- Offers to launch SAMY after installation.
- Registers a normal Windows uninstall entry.

## Get the installer

Open the repository's **Actions** tab, select the latest successful **Windows build and installer** run, and download the **SAMY-Windows-Installer** artifact. GitHub Actions artifacts are temporary build downloads, not a permanent public release.

## Build locally

Install .NET 10 SDK and Inno Setup 6 on Windows 11, then run from the repository root:

```powershell
dotnet publish .\src\Samy\Samy.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -o publish\win-x64
New-Item -ItemType Directory -Force artifacts | Out-Null
& "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe" .\installer\SAMY.iss
```

The installer is written to `artifacts\SAMY-Setup.exe`.

## Notes

- This first installer targets Windows 11 x64. ARM64 packaging is a separate build target.
- The app uses self-contained Windows App SDK deployment. The installer bundles the published application files.
- The installer is not code-signed yet. Windows SmartScreen may show an unknown-publisher warning until a trusted code-signing certificate is configured.
- The installer workflow must complete successfully before an installer artifact exists.
