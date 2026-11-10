using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

namespace EduCast.Studio;

internal sealed record CaptureWindow(nint Handle, uint ProcessId, string Title)
{
    public bool IsOpen => IsWindowVisible(Handle) && GetWindowThreadProcessId(Handle, out var id) != 0 && id == ProcessId;
    public bool IsMinimized => IsIconic(Handle);

    public static List<CaptureWindow> List()
    {
        var windows = new List<CaptureWindow>();
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var processId);
            if (processId == Environment.ProcessId || !IsWindowVisible(handle) ||
                (DwmGetWindowAttribute(handle, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0))
                return true;
            var title = new StringBuilder(512);
            if (GetWindowText(handle, title, title.Capacity) > 0 && !string.IsNullOrWhiteSpace(title.ToString()))
                windows.Add(new CaptureWindow(handle, processId, title.ToString()));
            return true;
        }, 0);
        return windows.OrderBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private delegate bool WindowCallback(nint handle, nint state);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowCallback callback, nint state);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int capacity);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
}

internal sealed class WindowCapture : IDisposable
{
    private const DirectXPixelFormat Format = DirectXPixelFormat.B8G8R8A8UIntNormalized;
    private readonly IDirect3DDevice device;
    private readonly Direct3D11CaptureFramePool pool;
    private readonly GraphicsCaptureSession session;
    private readonly GraphicsCaptureItem item;
    private SizeInt32 size;
    private Windows.Storage.Streams.Buffer? buffer;
    private byte[] pixels = [];
    private bool reading;
    private bool disposed;
    private volatile bool targetClosed;

    public CaptureWindow Target { get; }
    public bool IsOpen => !targetClosed && Target.IsOpen;

    public WindowCapture(CaptureWindow target)
    {
        if (!GraphicsCaptureSession.IsSupported() || !target.IsOpen || target.ProcessId == Environment.ProcessId)
            throw new InvalidOperationException("Windows capture is unavailable for that window.");
        Target = target;
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        interop.CreateForWindow(target.Handle, new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"), out var nativeItem);
        try { item = GraphicsCaptureItem.FromAbi(nativeItem); }
        finally { Marshal.Release(nativeItem); }
        size = item.Size;
        device = CreateDevice();
        try
        {
            pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, Format, 2, size);
            try
            {
                session = pool.CreateCaptureSession(item);
                try
                {
                    session.IsCursorCaptureEnabled = false;
                    item.Closed += TargetClosed;
                    session.StartCapture();
                }
                catch
                {
                    item.Closed -= TargetClosed;
                    session.Dispose();
                    throw;
                }
            }
            catch
            {
                pool.Dispose();
                throw;
            }
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    public async Task<BitmapSource?> ReadAsync()
    {
        if (disposed || reading)
            return null;
        reading = true;
        try
        {
            SizeInt32 content;
            using (var frame = pool.TryGetNextFrame())
            {
                if (frame is null)
                    return null;
                content = frame.ContentSize;
                if (content.Width < 1 || content.Height < 1)
                    return null;
                if (content.Width == size.Width && content.Height == size.Height)
                {
                    using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Ignore);
                    return disposed || !IsOpen || Target.IsMinimized ? null : ToImage(bitmap);
                }
            }
            pool.Recreate(device, Format, 2, content);
            size = content;
            return null;
        }
        finally
        {
            reading = false;
            if (disposed)
                CloseDevice();
        }
    }

    private BitmapSource ToImage(SoftwareBitmap bitmap)
    {
        var stride = checked(bitmap.PixelWidth * 4);
        var byteCount = checked(stride * bitmap.PixelHeight);
        if (buffer is null || pixels.Length != byteCount)
        {
            pixels = new byte[byteCount];
            buffer = new Windows.Storage.Streams.Buffer((uint)byteCount);
        }
        bitmap.CopyToBuffer(buffer);
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(pixels);
        var result = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96,
            PixelFormats.Bgr32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private void TargetClosed(GraphicsCaptureItem sender, object args) => targetClosed = true;

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        item.Closed -= TargetClosed;
        session.Dispose();
        if (!reading)
            CloseDevice();
    }

    private void CloseDevice()
    {
        pool.Dispose();
        device.Dispose();
    }

    private static IDirect3DDevice CreateDevice()
    {
        const uint bgraSupport = 0x20;
        const uint sdkVersion = 7;
        var result = D3D11CreateDevice(0, 1, 0, bgraSupport, 0, 0, sdkVersion, out var native, 0, 0);
        if (result < 0)
            result = D3D11CreateDevice(0, 5, 0, bgraSupport, 0, 0, sdkVersion, out native, 0, 0);
        Marshal.ThrowExceptionForHR(result);
        try
        {
            var dxgiId = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native, in dxgiId, out var dxgi));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var graphics));
                try { return MarshalInterface<IDirect3DDevice>.FromAbi(graphics); }
                finally { Marshal.Release(graphics); }
            }
            finally { Marshal.Release(dxgi); }
        }
        finally { Marshal.Release(native); }
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        void CreateForWindow(nint window, in Guid iid, out nint result);
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags,
        nint featureLevels, uint featureLevelsCount, uint sdkVersion, out nint device, nint featureLevel, nint context);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
}
