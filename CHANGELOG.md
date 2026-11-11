# Changelog

## 0.13.4

- **Geography** includes an offline world map and rotatable globe. Search for a country, territory, or city, zoom and pan, and show coordinate grid lines. The atlas uses Natural Earth data and can open the current area in OpenStreetMap for online detail.
- **Physics** adds an interactive simple pendulum. Change the length, release angle, and gravity, and compare its measured period with the small-angle prediction.
- **Biology** adds a quadrat for sampling a simulated field. **Chemistry** adds a graduated cylinder for reading water volume. Open the classroom **Stopwatch** directly from the Biology, Chemistry, or Physics tool list and read elapsed time to hundredths of a second.

## 0.13.3

- Double-click an existing board object or annotation from any canvas tool, then drag it to move. The eraser starts on a sweep, so a double-click can still select the writing.
- Click browser controls outside annotations even when an annotation was selected by double-click. The annotation under the cursor receives the click ahead of the page underneath. Hold **F4** to use a covered control, or click **Select** to draw a selection box.

## 0.13.2

- Pen taps and mouse dots stay in drawing mode. Double-click existing writing with the mouse or use **Select** to box a group, then drag to move or resize the selection.
- Sweep the eraser across the board or a teaching resource to remove the parts of strokes it touches. Choose from four sizes and follow the visible eraser outline and trail. Undo restores the entire sweep.
- Hold **F4** to use the teaching browser with an annotation tool or selection still active. Press **F4** to switch to a captured app.

## 0.13.1

- Toolbars distribute controls across the available width and wrap at smaller window sizes. Instrument settings expand into compact rows on both the board and teaching resources. Click **Instruments** again to collapse the settings.
- Choose **Violet**, **Ocean**, or **Daylight** from the **Theme** picker in either workspace. EduCast remembers the choice and updates open teacher windows immediately.
- Remove a selected object, the active instrument, or the shared clock with its visible **×** control on the teacher canvas.

## 0.13.0

- Keep several rulers, compasses, protractors, set squares and other instruments on the board or a teaching resource. Select and share each instrument separately.
- Compass arcs follow the pointer clockwise or counterclockwise and retain ink when you retrace a sweep.
- Students see shapes and objects move as you drag in Live mode. Move the published clock on the board or teaching resource. Press **Esc** for the selection cursor, or double-click an object to select it.
- **Smart ink** and **Smart math ink** automatically clean up recognized shapes and notation after a short pause. Use **Undo** to restore the original ink.
- Refreshed the workspace with a violet palette, mint action buttons, and distinct colors for creative tools and instruments.

## 0.12.1

- Fixed browser preview scaling so clicks and drags line up with local HTML lesson controls as the window is resized. Both **Open…** and **Open file…** use the same file routing, and the browser accepts pasted local paths.
- Open PowerPoint, Office, and OpenDocument files in their desktop apps from EduCast. For other file types, Windows lets you choose an app. Capture the live lesson or slideshow, then use **Interact in app** to return to its controls.
- Open a local browser file in another app when it needs a feature specific to that app. Captured windows refresh up to 25 times per second for smoother demonstrations and animations.

## 0.12.0

- Demonstrate a 0–150 mm vernier caliper and a 0–25 mm micrometer screw gauge. Both have shaded, movable bodies, adjustable measuring parts, and enlarged scales. Find them under **Instruments…** or in the Physics tool list.
- Slide the caliper for outside, inside, and depth measurements, or turn the micrometer thimble for outside measurements. Practice on specimens that stop the instrument at contact, or measure between points on a calibrated board.
- Show students the working scales, reveal or hide the calculation, and place a reading on the lesson as an editable label.

## 0.11.0

- Read the ruler’s zero and both protractors’ center and scale with the handles moved off their measuring faces. Compass pins show the exact pivot and pencil positions. Drag the pencil point with Trace off to set its reach and direction.
- Use **Smart math ink** to review and convert handwritten fractions, square roots, cube roots, superscripts, subscripts, and division signs. Undo restores the original ink.
- Sketch polygons, cones, and spheres with **Smart ink** or choose a shape from the tool list. The library includes cones, spheres, cubes, cuboids, cylinders, and polygons with up to twelve sides.

## 0.10.0

- Start and stop direct 720p broadcasts to multiple streaming destinations in EduCast. Each destination has its own connection status, and one disconnected destination leaves the others running.
- Stream approved board content, a teacher camera, and a selected microphone. Play and annotate local video or audio clips and include their sound in the broadcast. Blackout mutes streaming audio.
- The Windows installer and portable ZIP include EduCast’s media engine for streaming, cameras, media files, and recording.

## 0.9.0

- Switch between the board and the current file, web lesson or captured app with **Board** and **Resume source**. Use **Ctrl+Shift+B** to return from an external app. The source keeps its place and annotations.
- Pick custom page colors and graph, isometric or music-staff paper. Duplicate a page to work through another example or teaching step.
- Choose a subject per page. Chemistry formats and checks typed reactions and adds atoms, electron shells and bond diagrams. Biology, Physics, Geography and Mathematics add their own diagrams or notation tools.
- Sketch common shapes, arrows, equals signs and some circuit symbols in **Smart ink**. Apply a suggested replacement or keep the original strokes. The shape library also offers manual tools.
- Open and annotate common image files in the teaching browser.
- Open a dedicated **Online Program** window for Zoom, Teams or OBS while the classroom projector stays active. The setup guide covers the OBS virtual camera and multistreaming through Restream.

## 0.8.0

- Preview an open Windows app and share the selected window with the class.
- Open web links and local HTML lessons, SVGs, and PDFs in EduCast’s browser. Interactive lessons retain their JavaScript controls.
- Annotate browser pages and captured windows with icon buttons for round pen, square pen, chisel highlighter, eraser, and arrows. Use the arrow pointer to indicate a spot without leaving a mark. The board also has pen tip, arrow, and pointer tools.
- Drag board objects and source annotations to move them. Drag an edge or corner to resize ink, shapes, arrows, text, equations, and graphs proportionally.

## 0.7.0

- Added optional microphone audio to clean Program MP4 recordings. Select an input in Studio before each take. Video-only recording is the default.
- The teacher workspace indicates when a recording includes a microphone. The student display remains a visual output.

## 0.6.1

- Added an EduCast Studio logo to the teacher workspace and Windows app icon, including the Start menu, taskbar, and Installed apps.

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
