# Classroom milestone verification

## 0.5 on-board instruments

The Release build, whitespace check, and all 20 local checks passed with `--media`. The WPF demonstration check opened the tool selector, published and revoked a ruler, moved it while Program was frozen, and verified that the adjustment handle and calibration line stayed private. It also checked a committed ruler trace, compass arc, protractor construction, set-square edges, and French-curve vertex against the rendered student output. The calculator tests covered degree and radian results, keypad entry, and undefined input.

The UHD compositor check included a published ruler alongside camera and clock layers. Short 720p, 1080p and UHD media checks continued to pass. The mouse-coordinate checks exercise the board's gesture handlers with known points. Physical stylus and touch testing on classroom equipment remains open. [Instrument guide](MATH_INSTRUMENTS.md)

## 0.4 studio preview

The Release build passed with no compiler warnings. All 18 local checks passed with `--media` on x64 Windows using the installed FFmpeg 8.0 build.

| Check | Result |
| --- | --- |
| Classroom clock | Local time, analog mode, monotonic stopwatch, pause and countdown expiry passed |
| Device enumeration | FFmpeg reported 13 DirectShow video inputs. The parser excluded audio devices |
| Concurrent capture | Two generated FFmpeg inputs produced independent 640 × 360 camera frames |
| Clean media layers | Both camera positions and the analog clock rendered in the UHD Program. Blackout covered all layers, and unpublishing removed layers from a held frame |
| H.264 MP4 | FFprobe verified 1280 × 720, 1920 × 1080 and 3840 × 2160. The first frame of each file decoded and contained the expected camera pixels |

The recordings were short software-encoded tests with generated video. Physical camera capture, sustained UHD throughput, hardware encoding, audio and A/V sync remain open. [Studio test instructions](STUDIO.md#repeat-the-media-checks)

## 0.3 update notification

All 14 local checks passed. The release catalog tests covered a newer valid Windows installer, equal and older versions, draft and prerelease rejection, missing or pending installers, malformed tags, and a download URL supplied by the response. The WPF test verified that a newer version offered at startup appears as a **Download v…** button in the teacher workspace. The app constructs the versioned download from the official repository path.

## 0.2 classroom preview

The Release build and whitespace check passed using SDK 10.0.403. All 13 automated checks passed on x64 Windows build 26300.

| Check | Result |
| --- | --- |
| Native extended-display placement | Windows reported two 1920 × 1080 displays. The student HWND occupied Display 4 at its full monitor bounds, and the teacher window stayed on its original display. Both used 100% DPI |
| 4:3 fullscreen fit | A centered 4:3 viewport retained the lesson's 16:9 proportions |
| Display loss and recovery | A simulated topology change closed student output and preserved Program. Restoring the display selected it and waited for Start display |
| Progressive ink | The WPF test injected pressure-bearing samples before completing a stroke and verified the student pixels changed without adding history |
| Privacy during pen-down | Teacher-only strokes, unrevealed answers, manual Take, blackout and freeze kept the transient overlay out of the student view. Cancel cleared it |
| Object movement | The teacher saw the drag preview, Program received the committed move, and Undo restored the original coordinates and pressure samples |

Windows reported a ready pen digitizer through `SM_DIGITIZER`. Physical pen input, pressure accuracy, projector hot-plug, mixed DPI and ink latency still need hands-on tests. The [classroom hardware checklist](CLASSROOM_DISPLAY.md#physical-verification-checklist) describes those scenarios. [Microsoft digitizer flags](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getsystemmetrics)

All five ZIP entries matched the published files, and the packaged executable started. The unsigned MSI passed consistency checks with the expected ICE91 warnings for per-user installation. Clean-machine installation and upgrade tests remain open.

| 0.2 package | SHA-256 |
| --- | --- |
| Portable ZIP | `6843873B3BEAA738C6C1801B414D5626FB21DDEB43BE27B0C1CE7E2C2F775D55` |
| Per-user MSI | `13E67E7E68A3C4FCD6BFC5D0CFCC321EEDAAB7D4A2A39E22ED940D76719234A2` |

## 0.1 baseline

Tested November 9, 2026 on x64 Windows, build 26300. The Release build used .NET SDK 10.0.403 and Windows Desktop Runtime 10.0.14. Whitespace verification and compilation passed with zero compiler warnings and errors.

| Check | Result |
| --- | --- |
| Program projection and four visibility states | Passed |
| Manual Take, live updates, freeze, resume and blackout | Passed |
| Revocation from already published and frozen frames | Passed |
| Page navigation, undo/redo and private notes | Passed |
| Editable project round-trip, replacement and preservation after a failed save | Passed |
| Rejection of invalid project data | Passed |
| Syntax trees for fractions, powers, subscripts, radicals, matrices and chemistry | Passed |
| Rejection of malformed notation | Passed |
| Quadratic roots and vertex evaluation | Passed |
| WPF teacher startup and fit, stroke event handling, windowed student output and pixel comparisons | Passed |
| Decoded 1280 × 720 PNG and absence of private pixels | Passed |

WiX built the per-user MSI and the Windows Installer consistency checks returned success. ICE91 warned that the five app files install under the current user's profile. That matches the package's per-user scope. Install, upgrade and uninstall still need a clean-machine exercise. All five ZIP entries matched the published file hashes.

| Package | SHA-256 |
| --- | --- |
| Portable ZIP | `561D5538F509E12A8C05DEBA71FEE5DE6A2592BC0D914372E2DE8FAB97441B7E` |
| Per-user MSI | `1CB8DA3402D7D311734AC0B30003DA6E5082EFFBCB85621BC8109BDBA6CBBBEA` |

A single packaged-process smoke run reached the Windows message loop's idle state in 781 ms. The working set sampled at that point was 65.4 MiB. The measurement used PowerShell's stopwatch around process launch and `WaitForInputIdle`. It is specific to this run and does not measure time to first paint or latency during a lesson.

Physical projectors, display hot-plug, mixed DPI, stylus pressure and workload benchmarks remain open. Use the [classroom hardware checklist](CLASSROOM_DISPLAY.md#physical-verification-checklist) to collect that evidence. The [ManyCam audit](MANYCAM_FEATURE_PARITY.md) tracks the outstanding production checks.
