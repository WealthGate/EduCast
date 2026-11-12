using System.Text.Json;
using System.Text.Json.Serialization;

namespace EduCast.Classroom;

public static class LessonFile
{
    private const long MaxBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static Lesson Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes)
            throw new InvalidDataException("The lesson exceeds the 16 MB project limit.");
        try
        {
            var lesson = JsonSerializer.Deserialize<Lesson>(stream, Options);
            Validate(lesson);
            return lesson!;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("This file is not a valid EduCast lesson.", exception);
        }
    }

    public static void Save(string path, Lesson lesson)
    {
        Validate(lesson);
        AtomicFile.Write(path, stream =>
        {
            JsonSerializer.Serialize(stream, lesson, Options);
            if (stream.Length > MaxBytes)
                throw new InvalidDataException("The lesson exceeds the 16 MB project limit.");
        });
    }

    private static void Validate(Lesson? lesson)
    {
        if (lesson is null || lesson.Version != 1 || !Text(lesson.Title, 120) ||
            lesson.Pages.IsDefaultOrEmpty || lesson.Pages.Length > 200)
            throw new InvalidDataException("The lesson version, title or page count is invalid.");

        var ids = new HashSet<Guid>();
        foreach (var page in lesson.Pages)
        {
            if (page is null || !Text(page.Notes, 16000) || !Enum.IsDefined(page.Paper) ||
                !Enum.IsDefined(page.Subject) || page.Background is uint background && background >> 24 != 0xff ||
                page.Items.IsDefault || page.Items.Length > 3000)
                throw new InvalidDataException("The lesson contains an invalid page.");
            foreach (var item in page.Items)
            {
                if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) ||
                    !Enum.IsDefined(item.Visibility) || !ValidPlacement(item.Placement) || !ValidContent(item))
                    throw new InvalidDataException("The lesson contains an invalid board object.");
            }
        }
    }

    private static bool ValidPlacement(ObjectPlacement? placement) => placement is null ||
        Range(placement.Scale, ObjectPlacement.MinimumScale, ObjectPlacement.MaximumScale) &&
        Range(placement.X, -10_000_000, 10_000_000) && Range(placement.Y, -10_000_000, 10_000_000);

    private static bool ValidContent(Content content) => content switch
    {
        Ink ink => Enum.IsDefined(ink.Tip) && Range(ink.Width, 1, 60) && !ink.Points.IsDefaultOrEmpty &&
            ink.Points.Length <= 30000 && ink.Points.All(Point),
        Shape shape => Enum.IsDefined(shape.ShapeKind) && Range(shape.Width, 1, 60) &&
            Point(shape.Start) && Point(shape.End) && Range(shape.Sweep, -360, 360) &&
            (shape.Corner is not { } corner || shape.ShapeKind is ShapeKind.Triangle or ShapeKind.Rectangle && Point(corner)) &&
            (shape.ShapeKind == ShapeKind.Polygon
                ? shape.Vertices is { IsDefault: false, Length: >= 3 and <= 12 } vertices && vertices.All(Point)
                : shape.Vertices is null),
        Equation eq => Point(eq.Origin) && Range(eq.Size, 12, 96) &&
            Text(eq.Source, MathParser.MaxLength) && MathParser.TryParse(eq.Source, out _, out _),
        BoardText text => Point(text.Origin) && Range(text.Size, 12, 96) && Text(text.Text, 2000) &&
            (text.Style is not { } style || Text(style.Font, 128) && !string.IsNullOrWhiteSpace(style.Font) &&
                Range(style.Width, 80, 1280) && Range(style.Speed, 20, 250) && Enum.IsDefined(style.Case) &&
                Enum.IsDefined(style.Align) && Enum.IsDefined(style.Scroll)),
        BoardEffect effect => Point(effect.Origin) && Range(effect.Width, 1, 1280) && Range(effect.Height, 1, 720) &&
            Text(effect.Name, 100) && Text(effect.Data, BoardEffect.MaxImageBytes / 3 * 4) &&
            System.Buffers.Text.Base64.IsValid(effect.Data, out var bytes) && bytes is > 0 and <= BoardEffect.MaxImageBytes,
        Graph graph => Point(graph.Origin) && Text(graph.Source, MathParser.MaxLength) &&
            MathParser.TryParseGraph(graph.Source, out _, out _),
        ChemicalEquation equation => Point(equation.Origin) && Range(equation.Size, 12, 96) &&
            Chemistry.TryFormat(equation.Source, out _, out _, out _),
        TeachingDiagram diagram => Point(diagram.Origin) && Enum.IsDefined(diagram.Diagram) &&
            (diagram.Diagram == DiagramKind.Atom ? Chemistry.AtomicNumber(diagram.Element) is >= 1 and <= 20 :
                diagram.Element is null),
        _ => false
    };

    private static bool Text(string? value, int limit) => value is not null && value.Length <= limit;
    private static bool Point(BoardPoint point) => Range(point.X, -2048, 4096) &&
        Range(point.Y, -2048, 4096) && Range(point.Pressure, 0, 1);
    private static bool Range(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;
}
