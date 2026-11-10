# Teaching sources, cameras, classroom clock, and recording

## Teach from a web page, file, or app window

1. In the Program panel, choose **Browser…** to enter a web address or **Open file…** to load a local HTML lesson, PDF, or image. You can also open those files from the main **Open…** menu. Keep supporting files such as scripts and images in the lesson folder.
2. To teach from another app, choose **Capture window…**, select the open window, and choose **Preview window**. Work in the original app and annotate its preview in EduCast. Keep the captured window restored while sharing.
3. Select **Show on Program** when the preview is ready for students. In manual mode, use **Take preview** to publish the current source and its marks. **Freeze** holds the current picture and **Blackout** hides it.
4. Select **Interact** to use the page’s links, buttons, and sliders. Select a pen or highlighter icon to draw, or drag from tail to tip with **Draw arrow**. **Arrow pointer** follows the cursor and disappears when it leaves the preview. Annotations stay at their screen position and clear when you open another page or source. They are kept for the current session.
5. Choose **Select** to move an annotation by dragging it. Drag an edge or corner of its outline to resize it. Students see the change when you release the drag in live mode.
6. Use **← Board** or **Ctrl+Shift+B** to return to the writing board. Use **Resume source** to reopen the same material with its marks. In manual mode, the switch uses the last taken source and board. Use **Take preview** for new draft changes. Open slides in their original app and capture the slide-show window, or open a PDF copy in EduCast.

The browser uses the Microsoft Edge WebView2 Runtime. Local lessons with bundled assets can run offline. Browser and captured window sources share a picture, and recording audio still comes from the selected microphone. [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/#download-section)

## Studio controls

Open **Studio & clock…** in the teacher workspace. The clock works offline. The Windows release bundles EduCast’s FFmpeg-based media engine for DirectShow cameras and microphones, H.264/AAC recording, and live streaming. A developer checkout can use a separate FFmpeg executable for camera and recording checks. [FFmpeg licensing](https://www.ffmpeg.org/legal.html)

## Cameras

1. Select **Refresh devices** and choose an input for **Camera 1** or **Camera 2**.
2. Select **Start private preview**. If the device cannot start, check Windows camera permissions and whether another app is using it.
3. Select **Show on Program** when ready. Each camera occupies a separate 16:9 picture-in-picture position on the board. Select a different input for the second camera.
4. Clear **Show on Program** to remove a camera from the clean output, or select **Stop camera** to end capture. Private previews stay active until stopped or EduCast closes, including while the Studio panel is hidden.

A camera starts only when the teacher requests a private preview. Publishing a camera is a separate deliberate action. Freeze holds the published camera images and clock reading. Unpublishing removes the layer even while frozen. Blackout hides all layers. Starting a different lesson removes the published overlays.

Camera preview frames are scaled to 640 × 360 at 15 fps with letterboxing where needed. Capture quality depends on the camera, its driver, and USB bandwidth. Physical camera combinations still need hands-on testing.

## Clock

Choose **Digital**, **Analog**, **Stopwatch**, or **Countdown**. The clock views follow Windows local time. Stopwatch and countdown support start, pause and reset. Enter countdown duration as `MM:SS` or `H:MM:SS`, from one second to twelve hours.

Select **Show clock on Program** to place the clock in the upper-right corner of the student display. Leaving the controls closed lets a running timer continue. Countdown stops at zero.

## Record Program

1. Choose 720p, 1080p, or 4K UHD. The current recorder uses software H.264 at 15 fps.
2. For narration, select one **Recording microphone**. **No microphone** is the default for each take. Select **Refresh devices** after connecting a microphone. If your device is absent or busy, check the Windows input device and microphone permissions.
3. Select **Record Program…** and choose a destination. When a microphone is selected, the teacher toolbar shows **REC + mic** and the MP4 includes a stereo 48 kHz AAC track. The microphone starts with recording. Freeze and blackout affect video only, so use **Stop** to end audio capture.
4. Stop from the Studio panel or the red **REC** button. Wait for the saved-file message before opening the MP4. Closing EduCast while recording also requests finalization.

The recorder takes its frames from the clean Program composition. That includes approved board content, visible live ink, published cameras and the clock. It follows freeze and blackout. A connected projector is optional. The microphone is written to the MP4 and stays out of the student display and PNG exports. Recording currently uses one selected input at a time.

The UHD profile renders the board at 3840 × 2160. Camera layers still come from the preview-sized feeds. Sustained UHD throughput depends on the computer. If the encoder falls behind, choose a lower resolution. A failure can leave a `.partial.mp4` beside the intended file. The app reports its path when it detects the failure.

## Repeat the media checks

With `ffmpeg.exe` on PATH and `ffprobe.exe` beside it, run:

```powershell
dotnet run --project tests/classroom/EduCast.Checks.csproj -c Release -- --media
```

The checks enumerate DirectShow devices, use two generated camera feeds, and compare a generated audio cue with a matching Program picture change. For physical acceptance, run two private previews on different cameras, publish both, and record a lesson-length session with the selected microphone. Clap on camera at the beginning and end to check hardware A/V sync. Unplug each device during a test and verify the resulting video and device recovery. Measure sustained UHD performance on the target machine.
