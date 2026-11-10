# EduCast Studio

EduCast Studio is a Windows classroom preview with progressive ink and board object movement. It runs offline and keeps the teacher workspace separate from the fullscreen or windowed student output.

Open **Studio & clock…** for a classroom clock, two camera previews, and clean Program recording. The EduCast release includes its media engine.

Open **Instruments…** to put an interactive ruler, compass, protractor, set square, quadratic French curve, calculator, vernier caliper, or micrometer on the board. Select **Show on Program** when you want students to see the tool.

Open **Browser…** for web links and interactive HTML lessons, or choose **Capture window…** to preview another Windows app. Both support pen, highlighter, arrow, and pointer annotations before or during sharing. [Teaching sources](docs/STUDIO.md#teach-from-a-web-page-file-or-app-window)

Use **Board** and **Resume source** to switch between the writing board and your current teaching material. Choose a subject above the board for science notation, diagrams and sketch recognition. [Subject board](docs/SUBJECT_BOARD.md)

Choose **Live stream…** to broadcast from EduCast to multiple destinations, or **Online window…** to share Program in Zoom, Teams or Meet. [Teach online with EduCast](docs/ONLINE_TEACHING.md)

Get the installer or portable ZIP from [EduCast Releases](https://github.com/WealthGate/EduCast/releases). The teacher workspace checks for updates and can install newer versions when you click **Update**. For new-version notifications, choose **Watch → Custom → Releases** on GitHub.

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
- [Teaching sources, cameras, classroom clock, and recording](docs/STUDIO.md)
- [On-board math and physics instruments](docs/MATH_INSTRUMENTS.md)
- [Architecture and milestone roadmap](docs/ARCHITECTURE.md)
- [Scope and outstanding requirements](docs/REQUIREMENTS.md)
- [ManyCam 9 audit](docs/MANYCAM_FEATURE_PARITY.md)
- [Privacy boundary](docs/SECURITY.md)
- [Verification results and package checksums](docs/VERIFICATION.md)
- [Versioning and update notifications](docs/RELEASES.md)
