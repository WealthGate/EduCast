# Requirements status

The desktop is in Phase 6, multimedia integration, of the original roadmap. Earlier classroom phases have working implementations and the remaining scope below. The first Phase 7 capability is software-encoded UHD recording.

## Working in the classroom preview

- Local Windows teacher workspace with resizable side panels, board, notes, object visibility and Program monitor.
- Independently rendered student window, Win32 extended-display selection, windowed test mode, black, freeze, manual Take and live modes.
- Vector pen and highlighter with progressive clean output, configurable stroke width/color and smoothing, object erase and dragging, pages, history, board paper, teacher zoom, editable local save/load and clean PNG export.
- Ruler-style lines with 15° snapping, compass circles and arcs, rectangles, editable text, structured equation layout and a single-function plot on fixed −10 to 10 axes.
- On-board ruler calibration and metric or imperial scales, a compass with preserved radius, 180° and 360° protractors, two set squares, a quadratic French curve and a scientific calculator with optional student display.
- A bundled quadratic classroom example with CSEC-style factoring practice and optional answer reveal.
- A classroom clock with digital, analog, stopwatch and countdown modes, with optional clean output.
- Two private camera previews with explicit Program publishing. Clean Program MP4 recording at 720p, 1080p or UHD with an optional single DirectShow microphone through an external Windows FFmpeg installation.
- Versioned Windows releases with in-app updates and replacement of the previous installed version.
- Two-way board and teaching-source switching, page duplication, custom background colors, additional paper patterns, and subject-specific notation and diagrams.
- Assisted recognition for a bounded set of shapes and symbols, with the original strokes recoverable through Undo.
- A separate clean Online Program window alongside the projector, plus direct 720p multistreaming with microphone and local media audio. [Online teaching guide](ONLINE_TEACHING.md)

## Open or partially implemented

| Area | Outstanding scope |
| --- | --- |
| Classroom control | Physical projector hot-plug and mixed-DPI verification, on-screen display identification, selectable device resolution, output cursor, spotlight and independent compositions |
| Whiteboard and ink | Infinite canvas, lasso, layer reorder, locking and grouping, copy/paste, image/PDF/media annotation, advanced nibs and stabilization, physical stylus verification, tilt, palm rejection, touch pan and stylus eraser |
| Mathematics and geometry | Full LaTeX/MathML interchange and exports, symbol/template browser, calculus and advanced notation, recognition and correction, multiple simultaneous instruments, snapping to existing objects, calculator memory and unit conversions, advanced graphs and statistics |
| Subject teaching | Curated CSEC/CXC resources and standards mapping, broader diagram libraries, editable simulation parameters, and full handwriting-to-notation recognition |
| Production | Physical camera and audio device matrix, sustained hardware A/V sync, multiple microphones, system audio, media and slide import, scenes, hardware-accelerated 4K, streaming, native virtual camera, effects, integrations, network inputs and concurrent destinations |
| Accounts and schools | Identity, owner administration, school membership, tenant isolation, dashboard, beta invites, entitlement signing and revocation, audit trails, cloud storage and synchronization |
| Product and distribution | Mobile companion, optional AI, additional workspace presets and themes, signed deployment and school fleet support |

The next multimedia increment is local media and slide ingestion alongside physical camera and microphone validation. The [ManyCam audit](MANYCAM_FEATURE_PARITY.md) tracks the detailed production scope. The [classroom guide](CLASSROOM_DISPLAY.md) lists hardware acceptance steps.
