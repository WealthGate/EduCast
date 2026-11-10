using System.Collections.Immutable;
using System.IO;
using System.Windows;
using EduCast.Classroom;
using EduCast.Studio;
using Page = EduCast.Classroom.Page;
using Visibility = EduCast.Classroom.Visibility;

internal static partial class Checks
{
    private static void ChemicalNotation()
    {
        Assert(Chemistry.TryFormat("2 H2(g) + O2(g) -> 2 H2O(l)", out var text, out var balanced, out _) &&
            text == "2H₂(g) + O₂(g) → 2H₂O(l)" && balanced == true);
        Assert(Chemistry.TryFormat("Fe^3+(aq)+e^-<=>Fe^2+(aq)", out text, out balanced, out _) &&
            text == "Fe³⁺(aq) + e⁻ ⇌ Fe²⁺(aq)" && balanced == true);
        Assert(Chemistry.TryFormat(text, out var copied, out balanced, out _) && copied == text && balanced == true);
        Assert(Chemistry.TryFormat("Ca(OH)2 + 2HCl -> CaCl2 + 2H2O", out _, out balanced, out _) && balanced == true);
        Assert(Chemistry.TryFormat("CuSO4·5H2O -> CuSO4 + 5H2O", out text, out balanced, out _) &&
            text == "CuSO₄·5H₂O → CuSO₄ + 5H₂O" && balanced == true);
        Assert(Chemistry.TryFormat("Ag^+ + Cl^- -> AgCl(s)", out _, out balanced, out _) && balanced == true);
        Assert(Chemistry.TryFormat("Fe^3+ -> Fe^2+", out _, out balanced, out _) && balanced == false);
        Assert(Chemistry.TryFormat("H2 + O2 -> H2O", out _, out balanced, out _) && balanced == false);
        Assert(Chemistry.TryFormat("Ca(OH)2", out text, out balanced, out _) && text == "Ca(OH)₂" && balanced is null);
        foreach (var invalid in new[] { "Xx2", "H0", "H2 ->", "H2 +", "Ca(OH2", "e^+", "Fe^3", "H2->H2->H2" })
            Assert(!Chemistry.TryFormat(invalid, out _, out _, out _), invalid);
        Assert(Chemistry.Shells("Ca").SequenceEqual([2, 8, 8, 2]));
        Assert(Chemistry.Shells("Cl").SequenceEqual([2, 8, 7]));
        Assert(Chemistry.Shells("H").SequenceEqual([1]));
        Assert(Chemistry.ShellElements.Select(Chemistry.AtomicNumber).SequenceEqual(Enumerable.Range(1, 20)));
    }

    private static void SubjectPages() => InTemporaryDirectory(directory =>
    {
        var path = Path.Combine(directory, "science.educast");
        var old = Path.Combine(directory, "older.educast");
        File.WriteAllText(old, """{"version":1,"title":"Old","pages":[{"notes":"","paper":4,"answersRevealed":false,"items":[]}]}""");
        Assert(LessonFile.Load(old).Pages[0] is { Paper: BoardPaper.Dark, Subject: BoardSubject.General, Background: null });
        var session = new Session();
        var clean = new ChemicalEquation(Guid.NewGuid(), Visibility.Both, false, InkColor, new(90, 100), 42, "H2O");
        var atom = new TeachingDiagram(Guid.NewGuid(), Visibility.TeacherOnly, false, InkColor, new(80, 280),
            DiagramKind.Atom, true, "Na");
        var page = session.Page with
        {
            Subject = BoardSubject.Chemistry, Paper = BoardPaper.Isometric, Background = 0xff303c4d,
            Items = [clean, atom], Notes = "Private chemistry notes"
        };
        session.Live = false;
        session.ChangePage(page);
        Assert(session.Preview.Background == page.Background && session.Output?.Background is null);
        Assert(session.Preview.Items.SequenceEqual([clean]));
        session.Take();
        session.ToggleFreeze();
        session.ChangePage(page with { Background = 0xfffff0cc, Items = [clean with { Visibility = Visibility.Hidden }, atom] });
        Assert(session.Output is { } held && held.Background == page.Background && held.Items.IsEmpty);
        session.Undo();
        session.DuplicatePage();
        Assert(session.PageIndex == 1 && session.Page.Notes == page.Notes && session.Page.Subject == page.Subject);
        Assert(session.Page.Items.Zip(page.Items).All(pair => pair.First.Id != pair.Second.Id &&
            (pair.First with { Id = pair.Second.Id }) == pair.Second));
        LessonFile.Save(path, session.Lesson);
        var read = LessonFile.Load(path).Pages[1];
        Assert(read.Background == page.Background && read.Subject == BoardSubject.Chemistry &&
            read.Items[1] is TeachingDiagram { Element: "Na", ShowLabels: true });
        session.Undo();
        Assert(session.PageIndex == 0);
        session.AddPage();
        Assert(session.Page.Subject == BoardSubject.Chemistry && session.Page.Items.IsEmpty);
    });

    private static void SmartSketches()
    {
        Ink Sketch(params BoardPoint[] points) => new(Guid.NewGuid(), Visibility.TeacherOnly, true, InkColor, 4,
            true, false, points.ToImmutableArray());
        var line = Sketch(new(10, 50), new(43, 51), new(80, 50));
        var top = Sketch(new(10, 30), new(77, 29));
        Assert(SketchRecognition.Recognize(line, null, BoardSubject.General) is { ShapeKind: ShapeKind.Line });
        Assert(SketchRecognition.Recognize(line, top, BoardSubject.Mathematics) is
            { ShapeKind: ShapeKind.Equal, Visibility: Visibility.TeacherOnly, IsAnswer: true });
        Assert(SketchRecognition.Recognize(line, top, BoardSubject.Chemistry) is { ShapeKind: ShapeKind.DoubleBond });
        var plate = Sketch(new(80, 20), new(80, 80));
        var otherPlate = Sketch(new(62, 20), new(62, 81));
        Assert(SketchRecognition.Recognize(plate, otherPlate, BoardSubject.Physics) is { ShapeKind: ShapeKind.Capacitor });
        Assert(SketchRecognition.Recognize(plate, otherPlate, BoardSubject.Geography) is { ShapeKind: ShapeKind.Line });
        var resistor = Sketch(new(20, 90), new(40, 90), new(50, 77), new(63, 103), new(76, 77),
            new(89, 103), new(102, 77), new(115, 103), new(126, 90), new(145, 90));
        Assert(SketchRecognition.Recognize(resistor, null, BoardSubject.Physics) is { ShapeKind: ShapeKind.Resistor });
        Assert(SketchRecognition.Recognize(resistor, null, BoardSubject.Chemistry) is null);
        var triangle = Sketch(new(56, 10), new(100, 90), new(10, 92), new(56, 11));
        Assert(SketchRecognition.Recognize(triangle, null, BoardSubject.General) is { ShapeKind: ShapeKind.Triangle, Corner: not null });
        var rectangle = Sketch(new(40, 40), new(80, 20), new(95, 50), new(55, 70), new(40, 40));
        Assert(SketchRecognition.Recognize(rectangle, null, BoardSubject.General) is { ShapeKind: ShapeKind.Rectangle, Corner: not null });
        var circle = Sketch(Enumerable.Range(0, 37).Select(i =>
            new BoardPoint(70 + 40 * Math.Cos(i * Math.Tau / 36), 70 + 40 * Math.Sin(i * Math.Tau / 36))).ToArray());
        Assert(SketchRecognition.Recognize(circle, null, BoardSubject.General) is { ShapeKind: ShapeKind.Circle });
        Assert(SketchRecognition.Recognize(Sketch(new(10, 60), new(110, 60), new(84, 44), new(110, 60), new(86, 78)),
            null, BoardSubject.Physics) is { ShapeKind: ShapeKind.Arrow });
        Assert(SketchRecognition.Recognize(Sketch(new(16, 16), new(19, 24)), null, BoardSubject.General) is null);
    }

    private static void SubjectDrawing()
    {
        foreach (var kind in Enum.GetValues<DiagramKind>())
        {
            var diagram = new TeachingDiagram(Guid.NewGuid(), Visibility.Both, false, InkColor, new(80, 60),
                kind, true, kind == DiagramKind.Atom ? "Ca" : null);
            var bounds = BoardSurface.BoundsOf(diagram);
            Assert(!bounds.IsEmpty && bounds.Width > 60 && bounds.Height > 20, kind.ToString());
            var moved = (TeachingDiagram)BoardSurface.Move(diagram, new Vector(40, 50));
            Assert(moved.Origin == new BoardPoint(120, 110) && moved.Diagram == diagram.Diagram);
        }
    }
}
