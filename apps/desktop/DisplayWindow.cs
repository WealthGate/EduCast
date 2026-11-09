using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace EduCast.Studio;

internal sealed record Display(string Device, int Left, int Top, int Width, int Height, bool Primary)
{
    public string Label => $"{Device.Replace(@"\\.\DISPLAY", "Display ")}  ·  {Width} × {Height}" +
        (Primary ? "  ·  Primary" : "");
}

internal static class WindowsDisplays
{
    public const int DisplayChange = 0x007E;
    public const int DpiChange = 0x02E0;

    public static List<Display> Get()
    {
        var result = new List<Display>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (GetMonitorInfo(monitor, ref info))
            {
                var b = info.Bounds;
                result.Add(new Display(info.Device, b.Left, b.Top, b.Right - b.Left,
                    b.Bottom - b.Top, (info.Flags & 1) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return result.OrderBy(display => display.Device, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? For(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return null;
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
        return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info) ? info.Device : null;
    }

    public static void Place(Window window, Display display)
    {
        const uint flags = 0x0010 | 0x0200; // SWP_NOACTIVATE | SWP_NOOWNERZORDER
        if (!SetWindowPos(new WindowInteropHelper(window).Handle, new IntPtr(-1),
            display.Left, display.Top, display.Width, display.Height, flags))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Bounds;
        public NativeRect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, IntPtr bounds, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}

internal sealed class DisplayWindow : Window
{
    private readonly ProgramSurface board = new();
    private readonly AspectMatte matte;

    public DisplayWindow(Window teacher, Display? display)
    {
        Title = "EduCast · Student Output";
        Background = Brushes.Black;
        ShowActivated = false;
        matte = new AspectMatte { Background = Brushes.Black, IsHitTestVisible = false };
        matte.Children.Add(new Viewbox
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = board
        });
        Content = matte;

        if (display is not null)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Cursor = System.Windows.Input.Cursors.None;
            SourceInitialized += (_, _) => WindowsDisplays.Place(this, display);
        }
        else
        {
            Owner = teacher;
            Width = 800;
            Height = 490;
            MinWidth = 360;
            MinHeight = 250;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }

    public bool FourThree
    {
        get => matte.FourThree;
        set => matte.FourThree = value;
    }

    public void SetFrame(Classroom.ProgramFrame? frame) => board.Present(frame);

    public void SetLiveInk(Classroom.ProgramInk? ink) => board.PresentInk(ink);
    public void SetLayers(ProgramLayers layers) => board.PresentLayers(layers);

    private sealed class AspectMatte : Grid
    {
        private bool fourThree;

        public bool FourThree
        {
            get => fourThree;
            set
            {
                if (fourThree == value)
                    return;
                fourThree = value;
                InvalidateMeasure();
            }
        }

        protected override Size MeasureOverride(Size constraint) => base.MeasureOverride(Fit(constraint));

        protected override Size ArrangeOverride(Size size)
        {
            var fitted = Fit(size);
            Children[0].Arrange(new Rect((size.Width - fitted.Width) / 2,
                (size.Height - fitted.Height) / 2, fitted.Width, fitted.Height));
            return size;
        }

        private Size Fit(Size size)
        {
            if (!FourThree)
                return size;
            var width = Math.Min(size.Width, size.Height * 4 / 3);
            return new Size(width, width * 3 / 4);
        }
    }
}
