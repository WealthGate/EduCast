using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using EduCast.Classroom;
using Microsoft.Win32;

namespace EduCast.Studio;

internal sealed class StudioTools : Window
{
    private sealed class CameraSlot
    {
        public readonly ComboBox Choice = new() { DisplayMemberPath = nameof(CameraDevice.Name) };
        public readonly Image Preview = new() { Height = 96, Stretch = Stretch.Uniform };
        public readonly Button Start = new() { Content = "Start private preview" };
        public readonly CheckBox Publish = new() { Content = "Show on Program", IsEnabled = false };
        public CameraCapture? Capture;
        public string? DeviceId;
    }

    private readonly ClassroomClock clock = new(TimeProvider.System);
    private readonly CameraSlot[] cameras = [new(), new()];
    private readonly DispatcherTimer timer;
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock clockText = new() { FontSize = 32, FontWeight = FontWeights.SemiBold };
    private readonly TextBox countdown = new() { Text = "05:00", Width = 86, Margin = new Thickness(0, 0, 6, 5) };
    private readonly ComboBox clockMode = new() { ItemsSource = Enum.GetValues<ClockView>(), SelectedIndex = 0, Width = 115 };
    private readonly CheckBox clockPublish = new() { Content = "Show clock on Program" };
    private readonly Button toggleClock = new() { Content = "Start", IsEnabled = false };
    private readonly Button resetClock = new() { Content = "Reset", IsEnabled = false };
    private readonly ComboBox profiles = new()
    {
        ItemsSource = RecordingProfile.Options, DisplayMemberPath = nameof(RecordingProfile.Label), SelectedIndex = 0, Width = 205
    };
    private readonly Button record = new() { Content = "Record Program…" };
    private readonly Button scan = new() { Content = "Refresh cameras" };
    private bool closing;
    private bool recording;
    private string? ffmpeg = FfmpegMedia.Find();
    private ProgramLayers previous = new();

    public event Action<ProgramLayers>? LayersChanged;
    public event Action<string, RecordingProfile>? RecordRequested;
    public event Action? StopRequested;

    public StudioTools(Window teacher)
    {
        Owner = teacher;
        Title = "EduCast · Studio tools";
        Background = (Brush)Application.Current.FindResource("Shell");
        Foreground = (Brush)Application.Current.FindResource("Ink");
        Width = 680;
        Height = 595;
        MinWidth = 540;
        MinHeight = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(Label("CAMERAS  /  PRIVATE PREVIEW"));
        var setup = new WrapPanel();
        var browse = new Button { Content = "Choose FFmpeg…" };
        browse.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "FFmpeg (ffmpeg.exe)|ffmpeg.exe", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true)
                return;
            ffmpeg = dialog.FileName;
            await ScanAsync();
        };
        scan.Click += async (_, _) => await ScanAsync();
        setup.Children.Add(scan);
        setup.Children.Add(browse);
        root.Children.Add(setup);

        var slots = new UniformGrid { Columns = 2, Margin = new Thickness(0, 4, 0, 12) };
        for (var i = 0; i < cameras.Length; i++)
        {
            var slot = cameras[i];
            var controls = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            controls.Children.Add(Label($"Camera {i + 1}"));
            controls.Children.Add(slot.Choice);
            controls.Children.Add(new Border { Background = Brushes.Black, Child = slot.Preview });
            controls.Children.Add(slot.Start);
            controls.Children.Add(slot.Publish);
            slot.Start.Click += async (_, _) => await ToggleCameraAsync(slot);
            slot.Publish.Click += (_, _) => Refresh();
            slots.Children.Add(controls);
        }
        root.Children.Add(slots);

        root.Children.Add(Label("CLASSROOM CLOCK"));
        root.Children.Add(clockText);
        var clockControls = new WrapPanel();
        countdown.ToolTip = "Countdown duration in MM:SS or H:MM:SS";
        clockControls.Children.Add(clockMode);
        clockControls.Children.Add(countdown);
        clockControls.Children.Add(toggleClock);
        clockControls.Children.Add(resetClock);
        root.Children.Add(clockControls);
        root.Children.Add(clockPublish);
        clockMode.SelectionChanged += (_, _) => ConfigureClock();
        countdown.LostKeyboardFocus += (_, _) => ConfigureClock();
        toggleClock.Click += (_, _) => { if (ConfigureClock()) clock.Toggle(); Refresh(); };
        resetClock.Click += (_, _) => { if (ConfigureClock()) clock.Reset(); Refresh(); };
        clockPublish.Click += (_, _) => Refresh();

        root.Children.Add(Label("CLEAN PROGRAM RECORDING"));
        var recordingControls = new WrapPanel();
        recordingControls.Children.Add(profiles);
        recordingControls.Children.Add(record);
        root.Children.Add(recordingControls);
        record.Click += (_, _) =>
        {
            if (recording)
                StopRequested?.Invoke();
            else if (ffmpeg is not null && profiles.SelectedItem is RecordingProfile profile)
                RecordRequested?.Invoke(ffmpeg, profile);
            else
                Report("Choose a Windows FFmpeg build with the libx264 encoder.");
        };
        root.Children.Add(message);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Report(ffmpeg is null ? "Camera and recording require a separately installed Windows FFmpeg build."
            : "FFmpeg is available. Cameras stay private until you select Show on Program.");

        timer = new DispatcherTimer(TimeSpan.FromSeconds(1d / 15), DispatcherPriority.Background,
            (_, _) => Refresh(), Dispatcher);
        timer.Stop();
        IsVisibleChanged += (_, _) => Refresh();
        Loaded += async (_, _) => { if (ffmpeg is not null) await ScanAsync(); };
        Closing += (_, args) =>
        {
            if (closing)
                return;
            args.Cancel = true;
            Hide();
        };
        Refresh();
    }

    public void Report(string text) => message.Text = text;

    public void SetRecording(bool active, bool finishing = false)
    {
        recording = active;
        profiles.IsEnabled = !active;
        record.IsEnabled = !finishing;
        record.Content = finishing ? "Finishing…" : active ? "Stop recording" : "Record Program…";
    }

    public void Unpublish()
    {
        foreach (var slot in cameras)
            slot.Publish.IsChecked = false;
        clockPublish.IsChecked = false;
        Refresh();
    }

    public void Shutdown()
    {
        closing = true;
        timer.Stop();
        foreach (var slot in cameras)
            StopCamera(slot);
        Close();
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text, Margin = new Thickness(0, 5, 0, 5), FontWeight = FontWeights.SemiBold
    };

    private async Task ScanAsync()
    {
        if (ffmpeg is not { } executable || !scan.IsEnabled)
            return;
        scan.IsEnabled = false;
        try
        {
            var devices = await FfmpegMedia.CamerasAsync(executable);
            if (closing)
                return;
            foreach (var slot in cameras)
            {
                if (slot.Capture is not null)
                    continue;
                var selected = (slot.Choice.SelectedItem as CameraDevice)?.Id;
                slot.Choice.ItemsSource = devices;
                slot.Choice.SelectedItem = devices.FirstOrDefault(device => device.Id == selected);
                if (slot.Choice.SelectedItem is null && devices.Count > 0)
                    slot.Choice.SelectedIndex = 0;
            }
            Report(devices.Count == 0 ? "No DirectShow cameras were detected. Recording can still use the board."
                : $"{devices.Count} camera inputs found. Start a private preview to test a device.");
        }
        catch (Exception error) when (error is IOException or Win32Exception or OperationCanceledException)
        {
            if (!closing)
                Report("Could not enumerate cameras. Check the FFmpeg installation.");
        }
        finally { scan.IsEnabled = true; }
    }

    private async Task ToggleCameraAsync(CameraSlot slot)
    {
        if (slot.Capture is not null)
        {
            StopCamera(slot);
            Refresh();
            return;
        }

        if (ffmpeg is not { } executable || slot.Choice.SelectedItem is not CameraDevice device)
        {
            Report("Choose FFmpeg, refresh the camera list and select a device.");
            return;
        }
        if (cameras.Any(other => other.DeviceId == device.Id))
        {
            Report("Select a different input for the second camera.");
            return;
        }

        slot.Start.IsEnabled = false;
        slot.Choice.IsEnabled = false;
        slot.DeviceId = device.Id;
        try
        {
            var capture = new CameraCapture(executable, device);
            slot.Capture = capture;
            await capture.FirstFrame.WaitAsync(TimeSpan.FromSeconds(8));
            if (closing)
                return;
            slot.Publish.IsEnabled = true;
            slot.Start.Content = "Stop camera";
            Report("Camera preview is running privately.");
        }
        catch (Exception error) when (error is IOException or Win32Exception or TimeoutException)
        {
            StopCamera(slot);
            if (!closing)
                Report($"Could not start {device.Name}. Check Windows camera permissions and whether another app is using it.");
        }
        finally
        {
            slot.Start.IsEnabled = true;
            Refresh();
        }
    }

    private static void StopCamera(CameraSlot slot)
    {
        slot.Capture?.Dispose();
        slot.Capture = null;
        slot.DeviceId = null;
        slot.Preview.Source = null;
        slot.Choice.IsEnabled = true;
        slot.Publish.IsChecked = false;
        slot.Publish.IsEnabled = false;
        slot.Start.Content = "Start private preview";
    }

    private bool ConfigureClock()
    {
        if (clockMode.SelectedItem is not ClockView view)
            return false;
        var duration = TimeSpan.FromMinutes(5);
        if (view == ClockView.Countdown &&
            (!TimeSpan.TryParseExact(countdown.Text.Trim(), [@"m\:ss", @"h\:mm\:ss"],
                CultureInfo.InvariantCulture, out duration) ||
                duration < TimeSpan.FromSeconds(1) || duration > TimeSpan.FromHours(12)))
        {
            Report("Enter a countdown from 0:01 to 12:00:00.");
            return false;
        }
        clock.Select(view, duration);
        Refresh();
        return true;
    }

    private void Refresh()
    {
        if (closing)
            return;
        foreach (var slot in cameras)
        {
            if (slot.Capture?.Completion.IsCompleted == true && slot.Publish.IsEnabled)
            {
                StopCamera(slot);
                Report("A camera disconnected. Its Program overlay was removed.");
            }
            var image = slot.Capture?.Latest;
            if (IsVisible && !ReferenceEquals(slot.Preview.Source, image))
                slot.Preview.Source = image;
        }

        var reading = clock.Read();
        if (IsVisible && clockText.Text != reading.Text)
            clockText.Text = reading.Text;
        var controlsTime = reading.View is ClockView.Countdown or ClockView.Stopwatch;
        countdown.IsEnabled = clockMode.SelectedItem is ClockView.Countdown;
        toggleClock.IsEnabled = controlsTime && (reading.View != ClockView.Countdown || reading.Value > TimeSpan.Zero);
        resetClock.IsEnabled = controlsTime;
        toggleClock.Content = clock.Running ? "Pause" : "Start";

        var layers = new ProgramLayers(Published(cameras[0]), Published(cameras[1]),
            clockPublish.IsChecked == true ? reading : null);
        if (layers != previous)
        {
            previous = layers;
            LayersChanged?.Invoke(layers);
        }
        timer.IsEnabled = IsVisible || clockPublish.IsChecked == true || cameras.Any(slot => slot.Capture is not null);
    }

    private static System.Windows.Media.Imaging.BitmapSource? Published(CameraSlot slot) =>
        slot.Publish.IsChecked == true ? slot.Capture?.Latest : null;
}
