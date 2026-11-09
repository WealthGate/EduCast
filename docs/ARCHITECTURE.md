# Architecture and milestone roadmap

## Windows classroom foundation

The first desktop increment uses C# with nullable analysis and WPF on Windows. The brief suggests Rust and C++ for native work and React/TypeScript for interface development. WPF gives this milestone a Windows window and input stack, a vector compositor, and native stylus support without an embedded browser or web build dependencies. The lesson model and math parser target cross-platform .NET and have no dependency on WPF. The desktop UI would need a platform-specific port for macOS. [Microsoft WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/), [WPF digital ink](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/digital-ink)

| Location | Responsibility |
| --- | --- |
| `packages/classroom` | Immutable lesson and page records, board objects, local persistence, history, publication state, math syntax tree and graph evaluation |
| `apps/desktop` | Teacher workspace, WPF input and vector rendering, interactive geometry, Win32 display placement, clean window and snapshot export |
| `tests/classroom` | Core assertions and a WPF integration check |
| `docs` | Product scope, parity evidence, classroom verification and build instructions |

## Presentation boundary

The teacher edits an immutable `Lesson`, whose pages own notes, answer state and board objects. `ProgramFrame.From` removes teacher-only and hidden objects and unrevealed answers. Its constructor is private. The student window and PNG output receive the approved render frame only. The clean window has a dedicated HWND and contains a fitted board visual on a black matte. Selection borders and pending geometry remain in the teacher's overlay.

Immediate mode publishes committed ink and objects and can show approved ink while the pen is down. Manual mode keeps the most recently taken frame. Freeze holds the existing committed frame and allows the teacher to continue editing or take a new frame for display on resume. Blackout replaces the delivered frame with black while preserving both held and Program state. Privacy revocation removes newly restricted objects from held and previously taken frames without publishing other draft edits. Loading a different lesson stops external presentation.

Windows enumeration supplies physical monitor coordinates. The app uses a per-monitor DPI-aware manifest and positions the fullscreen window via Win32. Loss of the target display closes the student window and preserves the current lesson and Program state. Returning to presentation requires a deliberate restart. [Microsoft high-DPI desktop guidance](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)

## Rendering and offline files

- The board uses a 1280 × 720 logical coordinate space. WPF scales the vectors to the window. The output 4:3 setting fits the board inside a 4:3 matte without distorting geometry.
- The native InkCanvas renders in-progress pressure-sensitive strokes. Routed stylus packets or mouse samples feed a bounded overlay at WPF render priority. The session projects this overlay only for approved live ink. A completed stroke enters editable history once, using the native stroke's captured samples. Freeze, manual Take, and PNG export use committed frames. Drawings and paper patterns are reused across views.
- While the teacher drags a selected object, the teacher canvas translates its cached drawing in an overlay. Releasing the object commits the movement as one undoable edit.
- On-board instruments keep their pose and controls separate from lesson data. Trace gestures project onto mathematical guides and commit ordinary vector objects. Program receives a separately approved instrument display and the same privacy-filtered transient ink used for pen strokes. [Instrument guide](MATH_INSTRUMENTS.md)
- Equations are stored as source and parsed into a structured syntax tree. WPF typesets grouped expressions, fractions, scripts, radicals and small matrices. The graph evaluator accepts a constrained expression tree over real numbers. It never evaluates arbitrary code.
- Projects are bounded, versioned JSON files. A save writes a new file beside the destination, flushes it, then replaces the destination. The app validates incoming data before it replaces the active lesson. Notes remain in the editable project. Send the Program PNG for clean sharing.
- The desktop does not need a server. File failures appear in the app, and diagnostic entries contain an exception type and result code without lesson names or notes.

## Multimedia decisions

The preview uses a user-provided FFmpeg executable for DirectShow capture and software H.264 recording. Each camera capture reads a bounded latest frame into a private preview. The teacher explicitly publishes camera and clock layers. A shared Program surface composites the same approved board and published layers for the student window, still export and recorder. The recorder reuses unchanged raster frames and bounds its attempts to catch up under encoder load. Version 0.7 can mux one explicitly chosen DirectShow audio input into the MP4. Audio stays in the recorder and never enters the presentation model. See the [Studio guide](STUDIO.md).

| Candidate | Evaluation |
| --- | --- |
| Windows Media Foundation and WASAPI | First candidate for Windows capture, timing and hardware encoding. Keep native source adapters separate from board rendering. Verify hardware transforms and camera profiles on each supported tier. [Media Foundation SDK](https://learn.microsoft.com/en-us/windows/win32/medfound/microsoft-media-foundation-sdk) |
| Windows 11 virtual camera | Prototype a real Media Foundation custom media source reading approved Program frames. Validate conferencing-client enumeration, lifecycle and resolution negotiation before choosing a separate Windows 10 path. [Microsoft virtual camera API](https://learn.microsoft.com/en-us/windows/win32/api/mfvirtualcamera/nf-mfvirtualcamera-mfcreatevirtualcamera) |
| FFmpeg | Useful for offline imports and verification with ffprobe. Distribution depends on build flags, linked libraries, license notices and codec rights. No FFmpeg binaries ship in this milestone. [FFmpeg licensing](https://www.ffmpeg.org/legal.html) |
| OBS/libobs | A comprehensive reference for capture, composition and outputs. Direct integration requires a distribution and license review. [OBS Studio license](https://github.com/obsproject/obs-studio/blob/master/COPYING) |
| GStreamer | Candidate for modular cross-platform or network media. Review the selected plugin set and linked libraries before bundling. [GStreamer licensing](https://gstreamer.freedesktop.org/documentation/frequently-asked-questions/licensing.html) |

A media adapter should turn the approved composition into timestamped GPU surfaces. Presentation, recording, virtual camera and stream sinks should negotiate their own profiles and use bounded queues so a slow destination cannot stall writing. Benchmark hardware encode, frame copies, sync and dropped frames at each actual source and output resolution.

## Next milestones

1. Validate physical projector hot-plug, mixed DPI, stylus pressure and pen latency on classroom hardware. Progressive clean ink and automated dual-display placement landed in 0.2. Version 0.5 adds movable, publishable on-board instruments and guided construction.
2. Verify physical camera and microphone combinations and sustained A/V synchronization. Add local media and slide ingestion, system audio, and audio metering and controls.
3. Add hardware-accelerated 4K profiles, a real virtual camera, network sources, effects and streaming. Advance the parity matrix only with test evidence.
4. Add assisted handwriting recognition, broader math interchange and subject modules. Preserve original ink during correction and validate interpretation with educators.
5. Add owner provisioning, private beta invitations and school isolation. Use server-side tenant checks, scoped audit logs, privileged MFA, and offline entitlement expiry and revocation tests. Add secure mobile pairing and remote controls after desktop production paths are stable.
6. Prepare signed installers and update packages, backup and recovery workflows, deployment instructions and an institutional release checklist.
