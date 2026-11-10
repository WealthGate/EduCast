using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EduCast.Studio;

public partial class TeacherWindow
{
    private DisplayWindow? broadcastOutput;
    private BroadcastWindow? liveBroadcast;

    private void OpenLiveBroadcast(object sender, RoutedEventArgs args) => ShowLiveBroadcast();

    internal void ShowLiveBroadcast()
    {
        liveBroadcast ??= new BroadcastWindow(this);
        liveBroadcast.Show();
        liveBroadcast.Activate();
    }

    internal bool CanStartBroadcast => recording is null && !checkingUpdate && !applyingUpdate;

    internal (byte[] Video, bool AllowMic) CaptureBroadcast(ProgramVideoRenderer renderer, byte[] audio)
    {
        var approved = session.Blackout || session.Frozen || !sourceOnStage ? null : visibleLayers.Source?.MediaId;
        sourceWindow?.ReadMediaAudio(approved, audio);
        return (renderer.Capture(CurrentStudioFrame()), !session.Blackout);
    }

    internal void UpdateBroadcastStatus(int live, bool connecting, bool stopping = false)
    {
        LiveStreamButton.Content = stopping ? "Stopping stream…" : live > 0 ? $"● LIVE · {live}" :
            connecting ? "Connecting…" : "Live stream…";
        LiveStreamButton.Background = live > 0 ? Brushes.Firebrick : (Brush)FindResource("Panel");
    }

    private void OpenBroadcastSetup(object sender, RoutedEventArgs args)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        var dialog = BoardOptions("Online Program window", panel);
        panel.Children.Add(new TextBlock
        {
            Text = "Open a separate 16:9 window with just the Program feed. In Zoom, Teams or Meet, share that window. " +
                "To broadcast directly to streaming platforms, use Live stream in EduCast.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var guide = new Button { Content = "Open setup guide", HorizontalAlignment = HorizontalAlignment.Left };
        guide.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/WealthGate/EduCast/blob/main/docs/ONLINE_TEACHING.md")
                {
                    UseShellExecute = true
                });
            }
            catch (Win32Exception)
            {
                MessageBox.Show(dialog, "Windows could not open the default browser.", "Online teaching",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        panel.Children.Add(guide);
        AddDialogActions(dialog, panel, "Open Online Program");
        if (dialog.ShowDialog() == true)
            ShowBroadcastOutput();
    }

    private void OpenMediaFromBoard(object sender, RoutedEventArgs args) => OpenMediaSource();

    internal void ShowBroadcastOutput()
    {
        if (broadcastOutput is null)
        {
            var window = new DisplayWindow(this, null, online: true);
            window.SetFrame(session.Output);
            window.SetLiveInk(programInk);
            window.SetLayers(visibleLayers);
            window.Closed += (_, _) => broadcastOutput = null;
            window.Show();
            broadcastOutput = window;
        }
        if (broadcastOutput.WindowState == WindowState.Minimized)
            broadcastOutput.WindowState = WindowState.Normal;
        broadcastOutput.Activate();
        Report("Online Program is open. Share or capture that window by name.");
    }
}
