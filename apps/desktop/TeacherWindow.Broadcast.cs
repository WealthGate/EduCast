using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace EduCast.Studio;

public partial class TeacherWindow
{
    private DisplayWindow? broadcastOutput;

    private void OpenBroadcastSetup(object sender, RoutedEventArgs args)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        var dialog = BoardOptions("Zoom and streaming", panel);
        panel.Children.Add(new TextBlock
        {
            Text = "Open a separate 16:9 window with just the Program feed. In Zoom or Teams, share that window. " +
                "For a virtual camera or simultaneous broadcasts, capture it in OBS Studio and follow the setup guide.",
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
