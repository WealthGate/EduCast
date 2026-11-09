# EduCast Studio

EduCast Studio is a Windows classroom preview with progressive ink and board object movement. It runs offline and keeps the teacher workspace separate from the fullscreen or windowed student output.

Get the installer or portable ZIP from [EduCast Releases](https://github.com/WealthGate/EduCast/releases). For new-version notifications, choose **Watch → Custom → Releases** on GitHub. The teacher workspace also checks for updates.

## Start teaching

1. Open EduCast Studio and choose **Sample lesson** for a quadratic with a graph, private teacher notes, and a hidden answer.
2. Connect the projector and select **Extend** with **Win+P**. In Program, select the second display and choose **Start display**. Use **Windowed output** to check the workflow on one screen.
3. Write on the board or insert a graph or equation. Use **Freeze** or turn off **Live updates** to prepare the next step. Select **Take preview** to publish it.
4. Save an editable `.educast` project. Choose **PNG…** to export the current clean Program frame.

## Develop

Use the Windows x64 .NET 10 SDK. Run these commands from the repository root.

```powershell
dotnet build EduCast.slnx -c Release
dotnet run --project apps/desktop/EduCast.Studio.csproj -c Release
dotnet run --project tests/classroom/EduCast.Checks.csproj -c Release
```

The test executable returns a nonzero exit code if any check fails. Pass `-- --core-only` in an environment without an interactive Windows desktop.

- [Installation and build](docs/INSTALLATION.md)
- [Teacher guide and classroom hardware checklist](docs/CLASSROOM_DISPLAY.md)
- [Architecture and milestone roadmap](docs/ARCHITECTURE.md)
- [Scope and outstanding requirements](docs/REQUIREMENTS.md)
- [ManyCam 9 audit](docs/MANYCAM_FEATURE_PARITY.md)
- [Privacy boundary](docs/SECURITY.md)
- [Verification results and package checksums](docs/VERIFICATION.md)
- [Versioning and update notifications](docs/RELEASES.md)
