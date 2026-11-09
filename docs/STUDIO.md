# Cameras, classroom clock, and recording

Open **Studio & clock…** in the teacher workspace. The clock works offline with the desktop app. Camera capture and recording use a separately installed Windows FFmpeg build that supports DirectShow and the `libx264` encoder. EduCast looks for `ffmpeg.exe` on PATH. You can also select it with **Choose FFmpeg…**. [FFmpeg downloads](https://ffmpeg.org/download.html) · [FFmpeg licensing](https://www.ffmpeg.org/legal.html)

## Cameras

1. Select **Refresh cameras** and choose an input for **Camera 1** or **Camera 2**.
2. Select **Start private preview**. If the device cannot start, check Windows camera permissions and whether another app is using it.
3. Select **Show on Program** when ready. Each camera occupies a separate 16:9 picture-in-picture position on the board. Select a different input for the second camera.
4. Clear **Show on Program** to remove a camera from the clean output, or select **Stop camera** to end capture. Private previews stay active until stopped or EduCast closes, including while the Studio panel is hidden.

A camera starts only when the teacher requests a private preview. Publishing a camera is a separate deliberate action. Freeze holds the published camera images and clock reading. Unpublishing removes the layer even while frozen. Blackout hides all layers. Starting a different lesson removes the published overlays.

Camera preview frames are scaled to 640 × 360 at 15 fps with letterboxing where needed. Capture quality depends on the camera, its driver, and USB bandwidth. Physical camera combinations still need hands-on testing.

## Clock

Choose **Digital**, **Analog**, **Stopwatch**, or **Countdown**. The clock views follow Windows local time. Stopwatch and countdown support start, pause and reset. Enter countdown duration as `MM:SS` or `H:MM:SS`, from one second to twelve hours.

Select **Show clock on Program** to place the clock in the upper-right corner of the student display. Leaving the controls closed lets a running timer continue. Countdown stops at zero.

## Record Program

1. Choose 720p, 1080p, or 4K UHD. The current recorder uses software H.264 at 15 fps and produces video-only MP4.
2. Select **Record Program…** and choose a destination.
3. Stop from the Studio panel or the red **REC** button. Wait for the saved-file message before opening the MP4. Closing EduCast while recording also requests finalization.

The recorder takes its frames from the clean Program composition. That includes approved board content, visible live ink, published cameras and the clock. It follows freeze and blackout. A connected projector is optional.

The UHD profile renders the board at 3840 × 2160. Camera layers still come from the preview-sized feeds. Sustained UHD throughput depends on the computer. If the encoder falls behind, choose a lower resolution. A failure can leave a `.partial.mp4` beside the intended file. The app reports its path when it detects the failure.

## Repeat the media checks

With `ffmpeg.exe` on PATH and `ffprobe.exe` beside it, run:

```powershell
dotnet run --project tests/classroom/EduCast.Checks.csproj -c Release -- --media
```

The checks enumerate DirectShow devices and use two generated inputs for capture and recording. For physical acceptance, run two private previews on different devices, publish both, record for a lesson-length session, unplug one camera, and verify the resulting video and device recovery. Measure sustained UHD performance on the target machine before relying on it for a class.
