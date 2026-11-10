using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace EduCast.Classroom;

public enum Visibility { Both, TeacherOnly, StudentOnly, Hidden }
public enum BoardPaper { Plain, Grid, Dots, Ruled, Dark }
public enum ShapeKind { Line, Rectangle, Circle, Arc, Arrow }
public enum InkTip { Round, Square, Chisel }
public readonly record struct BoardPoint(double X, double Y, float Pressure = 0.5f);
public sealed record ObjectPlacement(double Scale, double X, double Y)
{
    public const double MinimumScale = .05;
    public const double MaximumScale = 16;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Ink), "ink")]
[JsonDerivedType(typeof(Shape), "shape")]
[JsonDerivedType(typeof(Equation), "equation")]
[JsonDerivedType(typeof(BoardText), "text")]
[JsonDerivedType(typeof(Graph), "graph")]
public abstract record Content(Guid Id, Visibility Visibility, bool IsAnswer, uint Argb)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ObjectPlacement? Placement { get; init; }
}

public sealed record Ink(Guid Id, Visibility Visibility, bool IsAnswer, uint Argb,
    double Width, bool Smooth, bool Highlighter, ImmutableArray<BoardPoint> Points,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] InkTip Tip = InkTip.Round)
    : Content(Id, Visibility, IsAnswer, Argb);

public sealed record Shape(Guid Id, Visibility Visibility, bool IsAnswer, uint Argb,
    double Width, ShapeKind ShapeKind, BoardPoint Start, BoardPoint End, double Sweep = 0)
    : Content(Id, Visibility, IsAnswer, Argb);

public sealed record Equation(Guid Id, Visibility Visibility, bool IsAnswer, uint Argb,
    BoardPoint Origin, double Size, string Source) : Content(Id, Visibility, IsAnswer, Argb);

public sealed record BoardText(Guid Id, Visibility Visibility, bool IsAnswer, uint Argb,
    BoardPoint Origin, double Size, string Text) : Content(Id, Visibility, IsAnswer, Argb);

public sealed record Graph(Guid Id, Visibility Visibility, bool IsAnswer, uint Argb,
    BoardPoint Origin, string Source) : Content(Id, Visibility, IsAnswer, Argb);

public sealed record Page(string Notes, BoardPaper Paper, bool AnswersRevealed,
    ImmutableArray<Content> Items)
{
    public static Page Empty() => new("", BoardPaper.Plain, false, []);
}

public sealed record Lesson(int Version, string Title, ImmutableArray<Page> Pages)
{
    public static Lesson Empty(string title = "Untitled lesson") => new(1, title, [Page.Empty()]);
}
