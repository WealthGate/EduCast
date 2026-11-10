using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EduCast.Studio;

internal sealed class BroadcastWindow : Window
{
    private sealed record MicChoice(string Label, MicrophoneDevice? Device);
    private sealed class DestinationRow(string name)
    {
        public string Name { get; } = name;
        public CheckBox Use { get; } = new() { Content = name };
        public TextBox Server { get; } = new() { MaxLength = 1024 };
        public PasswordBox Key { get; } = new() { MaxLength = 512, Margin = new Thickness(0, 3, 0, 5) };
        public TextBlock Status { get; } = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 20 };
    }

    private readonly TeacherWindow teacher;
    private readonly DestinationRow[] destinations = [new("YouTube"), new("Facebook"), new("X"), new("Twitch"), new("Custom")];
    private readonly ComboBox microphones = new()
    {
        Width = 255, DisplayMemberPath = nameof(MicChoice.Label), ItemsSource = new[] { new MicChoice("No microphone", null) },
        SelectedIndex = 0
    };
    private readonly Button scan = new() { Content = "Refresh microphones" };
    private readonly Button go = new() { Content = "Go live" };
    private readonly Button stop = new() { Content = "Stop streams", IsEnabled = false };
    private readonly CheckBox mute = new() { Content = "Mute microphone", IsEnabled = false, Margin = new Thickness(4, 8, 4, 0) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly DispatcherTimer timer;
    private DestinationRow[] current = [];
    private ProgramBroadcast? broadcast;
    private bool shuttingDown;
    private bool stopping;

    internal BroadcastWindow(TeacherWindow teacher)
    {
        this.teacher = teacher;
        Owner = teacher;
        Title = "EduCast · Live streaming";
        Width = 770;
        Height = 690;
        MinWidth = 620;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)FindResource("Shell");
        Foreground = (Brush)FindResource("Ink");
        var panel = new StackPanel { Margin = new Thickness(18) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var navigation = new WrapPanel();
        panel.Children.Add(navigation);
        AddButton(navigation, "← Board", teacher.ReturnToBoard);
        AddButton(navigation, "Camera preview…", teacher.ShowStudio);
        AddButton(navigation, "Media file…", teacher.OpenMediaSource);
        panel.Children.Add(new TextBlock
        {
            Text = "Stream approved Program video at 720p with a microphone and audio from published media clips. " +
                "Paste each provider's ingest URL and stream key separately.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 9)
        });
        var audio = new WrapPanel();
        AutomationProperties.SetName(microphones, "Streaming microphone");
        audio.Children.Add(microphones);
        audio.Children.Add(scan);
        audio.Children.Add(mute);
        panel.Children.Add(audio);
        panel.Children.Add(new TextBlock
        {
            Text = "DESTINATIONS     ·     SERVER URL AND STREAM KEY",
            Style = (Style)FindResource("Caption"), Margin = new Thickness(0, 10, 0, 4)
        });
        foreach (var row in destinations)
            panel.Children.Add(BuildRow(row));
        var controls = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        go.Style = (Style)FindResource("Primary");
        controls.Children.Add(go);
        controls.Children.Add(stop);
        panel.Children.Add(controls);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        panel.Children.Add(status);

        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(350), DispatcherPriority.Background,
            (_, _) => RefreshStatuses(), Dispatcher);
        timer.Stop();
        Loaded += async (_, _) => await ScanAsync();
        scan.Click += async (_, _) => await ScanAsync();
        go.Click += (_, _) => Start();
        stop.Click += (_, _) => Stop();
        mute.Click += (_, _) =>
        {
            if (broadcast is not null)
                broadcast.MicMuted = mute.IsChecked == true;
        };
        Closing += (_, args) =>
        {
            if (shuttingDown)
                return;
            args.Cancel = true;
            Hide();
        };
        if (FfmpegMedia.Bundled() is null)
            status.Text = "Install the complete EduCast release to use the embedded streaming engine.";
    }

    internal bool IsRunning => broadcast is not null;

    private void AddButton(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }

    private static FrameworkElement BuildRow(DestinationRow row)
    {
        row.Server.MinWidth = 260;
        row.Server.Margin = new Thickness(0, 3, 8, 5);
        row.Server.ToolTip = "Server URL, for example rtmps://ingest.example.com/live";
        row.Key.ToolTip = "Stream key supplied by the destination. Kept for this session.";
        AutomationProperties.SetName(row.Server, $"{row.Name} ingest URL");
        AutomationProperties.SetName(row.Key, $"{row.Name} stream key");
        var panel = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(94) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(row.Use);
        Grid.SetColumn(row.Server, 1);
        panel.Children.Add(row.Server);
        Grid.SetColumn(row.Key, 2);
        panel.Children.Add(row.Key);
        Grid.SetRow(row.Status, 1);
        Grid.SetColumn(row.Status, 1);
        Grid.SetColumnSpan(row.Status, 2);
        panel.Children.Add(row.Status);
        return panel;
    }

    private async Task ScanAsync()
    {
        if (FfmpegMedia.Bundled() is not { } executable)
            return;
        go.IsEnabled = scan.IsEnabled = false;
        try
        {
            var previous = (microphones.SelectedItem as MicChoice)?.Device?.Id;
            var found = await FfmpegMedia.DevicesAsync(executable);
            var choices = new[] { new MicChoice("No microphone", null) }
                .Concat(found.Microphones.Select(device => new MicChoice(device.Name, device))).ToArray();
            microphones.ItemsSource = choices;
            microphones.SelectedItem = choices.FirstOrDefault(choice => choice.Device?.Id == previous) ?? choices[0];
        }
        catch (Exception error) when (error is IOException or Win32Exception or OperationCanceledException)
        {
            status.Text = "Microphones could not be listed. Check Windows audio permissions.";
        }
        finally { go.IsEnabled = scan.IsEnabled = !shuttingDown; }
    }

    private void Start()
    {
        if (broadcast is not null || shuttingDown)
            return;
        if (FfmpegMedia.Bundled() is not { } executable)
        {
            status.Text = "The embedded streaming engine is missing. Install the complete EduCast release.";
            return;
        }
        if (!teacher.CanStartBroadcast)
        {
            status.Text = "Finish the update or stop the active recording before starting a stream.";
            return;
        }

        var active = destinations.Where(row => row.Use.IsChecked == true).ToArray();
        if (active.Length == 0)
        {
            status.Text = "Select at least one streaming destination.";
            return;
        }
        var settings = new List<StreamDestination>(active.Length);
        foreach (var row in active)
        {
            if (StreamDestination.Create(row.Name, row.Server.Text, row.Key.Password.Trim()) is not { } destination)
            {
                status.Text = $"Check {row.Name}'s server URL and key. Use RTMPS where available and keep the stream key in its own field.";
                return;
            }
            settings.Add(destination);
        }
        if (settings.DistinctBy(destination => (destination.Server, destination.Key)).Count() != settings.Count)
        {
            status.Text = "Each enabled destination must have a distinct address or key.";
            return;
        }

        current = active;
        foreach (var row in destinations)
        {
            row.Key.Clear();
            row.Status.Text = "";
        }
        mute.IsChecked = false;
        var renderer = new ProgramVideoRenderer(ProgramBroadcast.Profile);
        var job = new ProgramBroadcast(executable, settings, (microphones.SelectedItem as MicChoice)?.Device,
            (audio, cancel) => Dispatcher.InvokeAsync(() => teacher.CaptureBroadcast(renderer, audio),
                DispatcherPriority.Background, cancel).Task);
        broadcast = job;
        stopping = false;
        SetActive(true);
        timer.Start();
        RefreshStatuses();
        _ = ObserveAsync(job);
    }

    private void Stop()
    {
        if (broadcast is null || stopping)
            return;
        stopping = true;
        stop.IsEnabled = mute.IsEnabled = false;
        status.Text = "Stopping streams…";
        broadcast.Stop();
        teacher.UpdateBroadcastStatus(0, connecting: false, stopping: true);
    }

    private async Task ObserveAsync(ProgramBroadcast job)
    {
        string? error = null;
        try { await job.Completion; }
        catch (Exception failure)
        {
            App.Log(failure);
            error = "The streaming encoder stopped. Check Windows Media Foundation and try again.";
        }

        if (shuttingDown)
            return;
        RefreshStatuses();
        timer.Stop();
        broadcast = null;
        status.Text = error ?? (stopping ? "Streams stopped." : "No destinations are streaming. Check the status of each destination.");
        SetActive(false);
        teacher.UpdateBroadcastStatus(0, connecting: false);
    }

    private void SetActive(bool active)
    {
        go.IsEnabled = scan.IsEnabled = microphones.IsEnabled = !active;
        stop.IsEnabled = active;
        mute.IsEnabled = active && broadcast?.IncludesMic == true;
        foreach (var row in destinations)
            row.Use.IsEnabled = row.Server.IsEnabled = row.Key.IsEnabled = !active;
    }

    private void RefreshStatuses()
    {
        if (broadcast is not { } job)
            return;
        var states = job.Updates;
        for (var i = 0; i < states.Count; i++)
            current[i].Status.Text = $"{states[i].Status} · {states[i].Detail}";
        if (stopping)
            return;
        var live = states.Count(state => state.Status == StreamStatus.Live);
        var connecting = states.Any(state => state.Status == StreamStatus.Connecting);
        teacher.UpdateBroadcastStatus(live, connecting);
        var mic = !job.IncludesMic ? "No microphone selected." : job.MicEnded ? "Microphone unavailable." :
            job.MicMuted ? "Microphone muted." : "Microphone selected. Blackout mutes the broadcast audio.";
        status.Text = $"{live} of {states.Count} destinations sending. {mic}";
    }

    internal void Shutdown()
    {
        shuttingDown = true;
        timer.Stop();
        broadcast?.Stop();
    }
}
