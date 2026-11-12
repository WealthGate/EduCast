using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EduCast.Classroom;
using Microsoft.Win32;

namespace EduCast.Studio;

internal sealed record EffectAsset(string Name, string Data)
{
    internal BoardEffect Place(BoardPoint origin, EduCast.Classroom.Visibility visibility, bool answer = false)
    {
        var bitmap = EffectMedia.For(Data).Preview!;
        var scale = Math.Min(330d / bitmap.PixelWidth, 215d / bitmap.PixelHeight);
        var width = Math.Max(1, bitmap.PixelWidth * scale);
        var height = Math.Max(1, bitmap.PixelHeight * scale);
        var anchor = new BoardPoint(Math.Clamp(origin.X, 0, BoardSurface.BoardWidth - width),
            Math.Clamp(origin.Y, 0, BoardSurface.BoardHeight - height));
        return new BoardEffect(Guid.NewGuid(), visibility, answer, 0, anchor, width, height, Name, Data);
    }
}

internal sealed class EffectLibrary : Window
{
    private sealed record Preset(string Label, BoardSubject Subject, uint Ink, uint Paper, DiagramKind? Diagram = null);
    private sealed record Category(string Label, BoardSubject? Subject);
    private static readonly Preset[] Presets =
    [
        new("Gold star", BoardSubject.General, 0xffac6305, 0xffffebac),
        new("Parabola", BoardSubject.Mathematics, 0xff503c9b, 0xffeee6ff),
        new("Transverse wave", BoardSubject.Physics, 0xff125b9f, 0xffd5ecff, DiagramKind.TransverseWave),
        new("Oxygen atom", BoardSubject.Chemistry, 0xff9c3b72, 0xffffe5f0, DiagramKind.Atom),
        new("Plant cell", BoardSubject.Biology, 0xff147054, 0xffdbf8d9, DiagramKind.PlantCell),
        new("Compass rose", BoardSubject.Geography, 0xff854519, 0xffffe6c6, DiagramKind.CompassRose)
    ];
    private static readonly Dictionary<Preset, EffectAsset> BuiltIn = new();
    private readonly ComboBox categories = new() { MinWidth = 170, DisplayMemberPath = nameof(Category.Label) };
    private readonly ListBox choices = new() { DisplayMemberPath = nameof(Preset.Label) };
    private readonly Image preview = new() { Stretch = Stretch.Uniform, Height = 160, Margin = new Thickness(12) };
    private readonly TextBox address = new() { MaxLength = 4096, MinWidth = 230 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 26 };
    private readonly Button import = new() { Content = "Import file…" };
    private readonly Button download = new() { Content = "Download image" };
    private readonly Button insert = new() { Content = "Place effect" };
    private EffectAsset? current;

    internal EffectLibrary(Window owner, BoardSubject subject)
    {
        Owner = owner;
        Title = "EduCast · Effects";
        Width = 590;
        MinWidth = 490;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Panel");
        SetResourceReference(ForegroundProperty, "Ink");
        insert.SetResourceReference(StyleProperty, "Primary");
        AutomationProperties.SetName(categories, "Effect subject");
        AutomationProperties.SetName(choices, "Subject effects");
        AutomationProperties.SetName(address, "Direct HTTPS image link");

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "Effects", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        root.Children.Add(categories);
        var browser = new Grid { Height = 183, Margin = new Thickness(0, 4, 0, 10) };
        browser.ColumnDefinitions.Add(new ColumnDefinition());
        browser.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        browser.Children.Add(choices);
        var card = new Border { Child = preview, Margin = new Thickness(8, 0, 0, 0), CornerRadius = new CornerRadius(8) };
        card.SetResourceReference(Border.BackgroundProperty, "Field");
        Grid.SetColumn(card, 1);
        browser.Children.Add(card);
        root.Children.Add(browser);
        root.Children.Add(import);
        var link = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(download, Dock.Right);
        link.Children.Add(download);
        link.Children.Add(address);
        root.Children.Add(new TextBlock { Text = "Direct HTTPS image link", Margin = new Thickness(0, 5, 0, 2) });
        root.Children.Add(link);
        root.Children.Add(status);
        var actions = new ToolbarPanel { HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(insert);
        actions.Children.Add(new Button { Content = "Cancel", IsCancel = true });
        root.Children.Add(actions);
        Content = root;

        var options = new[] { new Category("All subjects", null) }
            .Concat(Enum.GetValues<BoardSubject>().Select(item => new Category(item.ToString(), item))).ToArray();
        categories.ItemsSource = options;
        categories.SelectionChanged += (_, _) =>
        {
            var filter = (categories.SelectedItem as Category)?.Subject;
            choices.ItemsSource = Presets.Where(preset => filter is null || preset.Subject == filter).ToArray();
            choices.SelectedIndex = 0;
        };
        choices.SelectionChanged += (_, _) =>
        {
            if (choices.SelectedItem is Preset preset)
                Present(Sticker(preset));
        };
        categories.SelectedItem = options.First(item => item.Subject == subject);

        var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var animation = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        animation.Tick += (_, _) =>
        {
            if (current is { } asset && EffectMedia.For(asset.Data) is { Animated: true } media)
                preview.Source = media.At(System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
        };
        Loaded += (_, _) => animation.Start();
        Closed += (_, _) =>
        {
            animation.Stop();
            cancellation.Cancel();
            cancellation.Dispose();
        };
        import.Click += async (_, _) =>
        {
            var file = new OpenFileDialog { Filter = "Pictures and GIFs (*.png;*.jpg;*.jpeg;*.gif)|*.png;*.jpg;*.jpeg;*.gif",
                CheckFileExists = true };
            if (file.ShowDialog(this) == true)
                await Load(Path.GetFileNameWithoutExtension(file.FileName), () => EffectMedia.FromFile(file.FileName, token), token);
        };
        download.Click += async (_, _) =>
        {
            var url = address.Text;
            await Load("Downloaded effect", () => EffectMedia.Download(url, token), token);
        };
        insert.Click += (_, _) =>
        {
            if (current is null)
                return;
            Result = current;
            DialogResult = true;
        };
    }

    internal EffectAsset? Result { get; private set; }

    private void Present(EffectAsset asset)
    {
        current = asset;
        var media = EffectMedia.For(asset.Data);
        preview.Source = media.Preview;
        status.Text = media.Animated ? $"{asset.Name} · Animated GIF" : asset.Name;
        insert.IsEnabled = preview.Source is not null;
    }

    private async Task Load(string name, Func<Task<string>> read, CancellationToken cancellationToken)
    {
        import.IsEnabled = download.IsEnabled = insert.IsEnabled = choices.IsEnabled = categories.IsEnabled = false;
        status.Text = "Loading image…";
        try
        {
            var data = await read();
            if (!cancellationToken.IsCancellationRequested)
            {
                choices.SelectedIndex = -1;
                Present(new EffectAsset(name[..Math.Min(100, name.Length)], data));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            HttpRequestException or OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
                status.Text = error is OperationCanceledException ? "The image download timed out." : error.Message;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                import.IsEnabled = download.IsEnabled = choices.IsEnabled = categories.IsEnabled = true;
                insert.IsEnabled = current is not null;
            }
        }
    }

    private static EffectAsset Sticker(Preset preset)
    {
        if (BuiltIn.TryGetValue(preset, out var result))
            return result;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var accent = BoardTypography.ColorBrush(preset.Ink);
            dc.DrawRoundedRectangle(BoardTypography.ColorBrush(preset.Paper), new Pen(accent, 3),
                new Rect(5, 5, 310, 203), 28, 28);
            if (preset.Subject == BoardSubject.General)
            {
                var star = new StreamGeometry();
                using (var geometry = star.Open())
                {
                    Point At(int i)
                    {
                        var angle = i * Math.PI / 5 - Math.PI / 2;
                        var radius = i % 2 == 0 ? 75 : 34;
                        return new Point(160 + radius * Math.Cos(angle), 106 + radius * Math.Sin(angle));
                    }
                    geometry.BeginFigure(At(0), true, true);
                    geometry.PolyLineTo(Enumerable.Range(1, 9).Select(At).ToArray(), true, false);
                }
                dc.DrawGeometry(Brushes.Gold, new Pen(accent, 4), star);
            }
            else
            {
                Classroom.Content art = preset.Diagram is { } diagram
                    ? new TeachingDiagram(Guid.NewGuid(), Classroom.Visibility.Both, false, preset.Ink, new(0, 0),
                        diagram, false, diagram == DiagramKind.Atom ? "O" : null)
                    : new Graph(Guid.NewGuid(), Classroom.Visibility.Both, false, preset.Ink, new(0, 0), "x^2");
                var bounds = BoardSurface.BoundsOf(art);
                var scale = Math.Min(270 / bounds.Width, 164 / bounds.Height);
                dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, 160 - (bounds.X + bounds.Width / 2) * scale,
                    106 - (bounds.Y + bounds.Height / 2) * scale));
                BoardSurface.DrawItems(dc, [art]);
                dc.Pop();
            }
        }
        var bitmap = new RenderTargetBitmap(320, 213, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        png.Save(output);
        result = new EffectAsset(preset.Label, Convert.ToBase64String(output.GetBuffer(), 0, (int)output.Length));
        BuiltIn.Add(preset, result);
        return result;
    }
}
