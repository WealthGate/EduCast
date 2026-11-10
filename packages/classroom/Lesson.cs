using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace EduCast.Classroom;

public enum Visibility { Both, TeacherOnly, StudentOnly, Hidden }
public enum BoardPaper { Plain, Grid, Dots, Ruled, Dark, Graph, Isometric, Staff }
public enum BoardSubject { General, Mathematics, Physics, Chemistry, Biology, Geography }
public enum ShapeKind
{
    Line, Rectangle, Circle, Arc, Arrow, Ellipse, Triangle, Resistor, Capacitor, Equal, DoubleBond, TripleBond,
    Polygon, Cone, Sphere, Cube, Cuboid, Cylinder
}
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
[JsonDerivedType(typeof(ChemicalEquation), "chemicalEquation")]
[JsonDerivedType(typeof(TeachingDiagram), "teachingDiagram")]
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
    : Content(Id, Visibility, IsAnswer, Argb)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BoardPoint? Corner { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<BoardPoint>? Vertices { get; init; }
}

public sealed record Equation(Guid Id, Visibility Visibility, bool IsAnswer, uint Argb,
    BoardPoint Origin, double Size, string Source) : Content(Id, Visibility, IsAnswer, Argb);

public sealed record BoardText(Guid Id, Visibility Visibility, bool IsAnswer, uint Argb,
    BoardPoint Origin, double Size, string Text) : Content(Id, Visibility, IsAnswer, Argb);

public sealed record Graph(Guid Id, Visibility Visibility, bool IsAnswer, uint Argb,
    BoardPoint Origin, string Source) : Content(Id, Visibility, IsAnswer, Argb);

public sealed record ChemicalEquation(Guid Id, Visibility Visibility, bool IsAnswer, uint Argb,
    BoardPoint Origin, double Size, string Source) : Content(Id, Visibility, IsAnswer, Argb);

public sealed record TeachingDiagram(Guid Id, Visibility Visibility, bool IsAnswer, uint Argb,
    BoardPoint Origin, DiagramKind Diagram, bool ShowLabels, string? Element = null)
    : Content(Id, Visibility, IsAnswer, Argb);

public sealed record Page(string Notes, BoardPaper Paper, bool AnswersRevealed,
    ImmutableArray<Content> Items)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public BoardSubject Subject { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public uint? Background { get; init; }

    public static Page Empty() => new("", BoardPaper.Plain, false, []);
}

public sealed record Lesson(int Version, string Title, ImmutableArray<Page> Pages)
{
    public static Lesson Empty(string title = "Untitled lesson") => new(1, title, [Page.Empty()]);
}
