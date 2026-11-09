# Changelog

## 0.6.0

- Added in-app updates that verify the GitHub installer, install it after EduCast closes, and reopen the updated app.
- Windows Installer replaces the previous installed release as part of the upgrade. Successful release builds also prune recognized older local packages and portable binaries.

## 0.5.0

- Added movable on-board rulers, compasses, 180° and 360° protractors, two set squares, and a quadratic French curve.
- Added calibrated metric and imperial ruler scales, guided construction and tracing, and measurement labels.
- Added a scientific calculator with an on-board readout, degree and radian modes, optional keypad, and private calculation history.
- Added explicit sharing of the current instrument to Program, including the student display and recordings.

## 0.4.0

- Added two DirectShow camera preview slots with separate controls for private preview and publishing to Program.
- Added video-only H.264 MP4 recording of the clean Program at 720p, 1080p, and 4K UHD. Camera capture and recording use a separate Windows FFmpeg installation.
- Added a digital clock, analog clock, stopwatch, and countdown timer with optional student-display output.

## 0.3.0

- The teacher workspace checks GitHub for a newer version at startup. **Check for updates** checks again on request, and **Download v…** opens the newer Windows installer in the browser.
- A pushed version tag runs the Windows checks and publishes the MSI, portable ZIP and SHA-256 manifest on GitHub Releases.

## 0.2.0

- Approved live ink reaches the student display while the teacher writes.
- Board objects can be repositioned with **Select** and restored with Undo.
- Tested fullscreen placement on a second Windows display, centered 4:3 output and simulated display loss and recovery.

## 0.1.0

- Initial local classroom build with separate teacher and student windows, editable vector ink, lessons, equations, graphs and clean PNG export.
