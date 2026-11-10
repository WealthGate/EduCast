# Windows installation and packaging

## Install the classroom build

1. Use x64 Windows 11, or a Windows 10 edition and release that Microsoft still supports for the desktop runtime. The milestone was exercised on Windows 11. [Microsoft Windows support matrix](https://learn.microsoft.com/en-us/dotnet/core/install/windows#supported-versions)
2. Install the x64 .NET 10 **Desktop Runtime**. [Microsoft .NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
3. Download the x64 MSI from [EduCast Releases](https://github.com/WealthGate/EduCast/releases), run it, and open **EduCast Studio** from Start. The MSI installs for the current user and is unsigned. Schools with publisher restrictions require a signed package for managed distribution.
4. For portable use, extract the entire matching ZIP and run `EduCast.Studio.exe` from that folder.
5. Choose **Sample lesson** and follow the [classroom setup](CLASSROOM_DISPLAY.md).

The teaching browser uses the Microsoft Edge WebView2 Evergreen Runtime. If EduCast prompts for it, install the x64 runtime and reopen the browser. [Microsoft WebView2 downloads](https://developer.microsoft.com/microsoft-edge/webview2/#download-section)

Uninstall through **Settings → Apps → Installed apps**. Keep your lesson files in a separate working folder. Back up the editable `.educast` files through your usual local backup process. Reopen a backup through **Open…**. A Program PNG is suitable for clean sharing, while an editable lesson includes its teacher notes and all objects.

For upgrades and notifications, see [Releases and update notifications](RELEASES.md).

## Build from source

Install the x64 .NET 10 SDK and open PowerShell at the repository root.

```powershell
dotnet build EduCast.slnx -c Release
dotnet run --project apps/desktop/EduCast.Studio.csproj -c Release
dotnet run --project tests/classroom/EduCast.Checks.csproj -c Release
```

Generate the ZIP only with:

```powershell
./scripts/Build-Release.ps1 -SkipInstaller
```

Generate the ZIP and MSI with WiX 5.0.2. WiX runs only during packaging and is separate from the installed app. [WiX tool package](https://www.nuget.org/packages/wix/5.0.2)

```powershell
dotnet tool install wix --version 5.0.2 --tool-path ./artifacts/tools --allow-roll-forward
./scripts/Build-Release.ps1 -Wix ./artifacts/tools/wix.exe
```

The script writes the packages and the SHA-256 manifest. Use its `-DotNet` argument when building with a portable SDK. The Windows workflow builds a preview on branches and publishes GitHub Releases for version tags. See the [verification report](VERIFICATION.md) for milestone checks.

## Deployment scope

The classroom build uses local files and works offline once the runtime is installed. See the [requirements status](REQUIREMENTS.md) for the backend, device permissions, virtual camera, cloud, account provisioning, and updates planned for later milestones.
