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
                page.Items.IsDefault || page.Items.Length > 3000)
                throw new InvalidDataException("The lesson contains an invalid page.");
            foreach (var item in page.Items)
            {
                if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) ||
                    !Enum.IsDefined(item.Visibility) || !ValidContent(item))
                    throw new InvalidDataException("The lesson contains an invalid board object.");
            }
        }
    }

    private static bool ValidContent(Content content) => content switch
    {
        Ink ink => Range(ink.Width, 1, 60) && !ink.Points.IsDefaultOrEmpty &&
            ink.Points.Length <= 30000 && ink.Points.All(Point),
        Shape shape => Enum.IsDefined(shape.ShapeKind) && Range(shape.Width, 1, 60) &&
            Point(shape.Start) && Point(shape.End) && Range(shape.Sweep, -360, 360),
        Equation eq => Point(eq.Origin) && Range(eq.Size, 12, 96) &&
            Text(eq.Source, MathParser.MaxLength) && MathParser.TryParse(eq.Source, out _, out _),
        BoardText text => Point(text.Origin) && Range(text.Size, 12, 96) && Text(text.Text, 2000),
        Graph graph => Point(graph.Origin) && Text(graph.Source, MathParser.MaxLength) &&
            MathParser.TryParseGraph(graph.Source, out _, out _),
        _ => false
    };

    private static bool Text(string? value, int limit) => value is not null && value.Length <= limit;
    private static bool Point(BoardPoint point) => Range(point.X, -2048, 4096) &&
        Range(point.Y, -2048, 4096) && Range(point.Pressure, 0, 1);
    private static bool Range(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;
}
