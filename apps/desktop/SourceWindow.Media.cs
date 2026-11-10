using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Visibility = System.Windows.Visibility;

namespace EduCast.Studio;

internal partial class SourceWindow
{
    private string? mediaPath;
    private Guid? mediaId;
    private MediaPlayback? playback;
    private TimeSpan mediaPosition;
    private BitmapSource? audioSlate;

    private void SelectMediaFile(object sender, RoutedEventArgs args) => ChooseMediaFile();

    internal bool ChooseMediaFile()
    {
        var dialog = new OpenFileDialog { Filter = MediaPlayback.Filter, CheckFileExists = true };
        if (dialog.ShowDialog(this) != true)
            return false;
        OpenMediaFile(dialog.FileName);
        return true;
    }

    internal void OpenMediaFile(string file)
    {
        ResetSource();
        browserMode = false;
        BrowserBar.Visibility = Visibility.Collapsed;
        BrowserSlot.Visibility = Visibility.Hidden;
        WindowBar.Visibility = Visibility.Collapsed;
        MediaBar.Visibility = Visibility.Visible;
        mediaPath = file;
        mediaId = Guid.NewGuid();
        MediaName.Text = Path.GetFileName(file);
        timer.Interval = TimeSpan.FromMilliseconds(40);
        SetTool(Tool.Pen);
        PlayMedia();
    }

    internal SourceFrame? AnimateTakenMedia(SourceFrame? frame) =>
        frame is { MediaId: Guid id } && id == mediaId && image is not null ? frame with { Image = image } : frame;

    internal void ReadMediaAudio(Guid? approved, Span<byte> buffer)
    {
        if (playback is null)
            return;
        if (approved.HasValue && approved == mediaId && Publish.IsChecked == true)
            playback.Audio.Read(buffer);
        else
            playback.Audio.Clear();
    }

    internal void PauseMedia()
    {
        if (playback is null)
            return;
        mediaPosition = playback.Position;
        playback.Dispose();
        playback = null;
        PlayMediaButton.Content = "Play";
        Hint.Text = "Clip paused. Select Play to resume.";
    }

    private void ToggleMedia(object sender, RoutedEventArgs args)
    {
        if (playback is null)
            PlayMedia();
        else
            PauseMedia();
    }

    private void RestartMedia(object sender, RoutedEventArgs args)
    {
        PauseMedia();
        mediaPosition = TimeSpan.Zero;
        PlayMedia();
    }

    private void PlayMedia()
    {
        if (mediaPath is null)
            return;
        if (FfmpegMedia.Find() is not { } executable)
        {
            Hint.Text = "The embedded media engine is missing. Install the complete EduCast release.";
            return;
        }
        try
        {
            playback = new MediaPlayback(executable, mediaPath, mediaPosition);
            PlayMediaButton.Content = "Pause";
            Hint.Text = "Playing clip. Select Show on Program when ready.";
        }
        catch (Exception error) when (error is IOException or Win32Exception)
        {
            Hint.Text = "Could not open the media file.";
        }
    }

    private void RefreshMedia()
    {
        if (playback is not { } media)
            return;
        if (media.Image is { } next && image != next)
        {
            image = next;
            RefreshOverlay();
        }
        else if (media.Started && image is null)
        {
            image = audioSlate ??= MakeAudioSlate();
            RefreshOverlay();
        }
        if (media.Completion.IsCompleted)
        {
            var succeeded = media.Completion.IsCompletedSuccessfully && media.Completion.Result;
            media.Dispose();
            playback = null;
            mediaPosition = TimeSpan.Zero;
            PlayMediaButton.Content = "Play";
            Hint.Text = succeeded ? "Clip finished. Select Play to replay." : "Could not play this media file.";
        }
    }

    private static BitmapSource MakeAudioSlate()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 960, 540));
            dc.DrawText(BoardSurface.Format("Audio clip", 46, Brushes.White), new Point(370, 225));
        }
        var frame = new RenderTargetBitmap(960, 540, 96, 96, PixelFormats.Pbgra32);
        frame.Render(visual);
        frame.Freeze();
        return frame;
    }

    private void ResetMedia()
    {
        playback?.Dispose();
        playback = null;
        mediaPath = null;
        mediaId = null;
        mediaPosition = TimeSpan.Zero;
        PlayMediaButton.Content = "Play";
        timer.Interval = TimeSpan.FromMilliseconds(125);
    }
}
