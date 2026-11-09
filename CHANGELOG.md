# Changelog

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
