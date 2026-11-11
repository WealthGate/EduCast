using System.IO;
using System.Windows;
using System.Windows.Input;
using EduCast.Classroom;
using EduCast.Studio;
using Visibility = EduCast.Classroom.Visibility;

internal static partial class Checks
{
    private static void InkTapGestures()
    {
        var dot = new Ink(Guid.NewGuid(), Visibility.Both, false, InkColor, 4, true, false, [new(100, 100)]);
        var dots = new List<Ink>();
        var taps = new InkTap(dots.Add);
        var existing = Guid.NewGuid();

        taps.Begin(1, null, physicalMouse: true);
        Assert(taps.Collect(dot) is null && dots.Count == 1);
        taps.Begin(2, dot.Id, physicalMouse: true);
        Assert(taps.Collect(dot) is null && dots.Count == 2, "a just-drawn dot cannot be the first selection target");

        taps.Begin(1, existing, physicalMouse: false);
        Assert(taps.Collect(dot) is null);
        taps.Begin(2, existing, physicalMouse: false);
        Assert(taps.Collect(dot) is null && dots.Count == 4, "rapid stylus taps must keep drawing");

        taps.Begin(1, existing, physicalMouse: true);
        Assert(taps.Collect(dot) is null && dots.Count == 4);
        taps.Begin(2, existing, physicalMouse: true);
        var line = dot with { Points = [new(100, 100), new(190, 110)] };
        Assert(taps.Collect(line) is null && dots.Count == 6 && dots[^1] == line,
            "dragging on the second press must complete a stroke");

        taps.Begin(1, existing, physicalMouse: true);
        Assert(taps.Collect(dot) is null);
        taps.Begin(2, existing, physicalMouse: true);
        Assert(Equals(taps.Collect(dot), existing) && dots.Count == 6);
    }

    private static void CanvasSweeps()
    {
        var ink = new Ink(Guid.NewGuid(), Visibility.Both, false, InkColor, 8, true, true,
            [new(100, 100, .2f), new(200, 100, .5f), new(300, 100, .8f)], InkTip.Chisel)
        {
            Placement = new ObjectPlacement(2, 0, 0)
        };
        var dot = ink with { Id = Guid.NewGuid(), Points = [new(200, 150)] };
        var student = dot with { Id = Guid.NewGuid(), Visibility = Visibility.StudentOnly };
        var arrow = new Shape(Guid.NewGuid(), Visibility.TeacherOnly, false, InkColor, 4,
            ShapeKind.Arrow, new(350, 350), new(460, 350));
        var session = new Session();
        session.ChangePage(session.Page with { Items = [ink, dot, student, arrow] });
        var original = session.Page.Items;
        var next = BoardSurface.Erase(original, new Point(400, 80), new Point(400, 400), 24);
        var fragments = next.OfType<Ink>().Where(item => item != student).ToArray();
        Assert(fragments.Length == 2 && next.Length == 3 && next[^1] == student);
        Assert(fragments[0].Id == ink.Id && fragments[1].Id != ink.Id &&
            fragments.All(item => item.Placement == ink.Placement && item.Tip == ink.Tip && item.Highlighter));
        Assert(BoardSurface.BoundsOf(fragments[0]).Right < 397 && BoardSurface.BoundsOf(fragments[1]).Left > 403,
            "the eraser must leave a gap in the scaled stroke");
        Assert(BoardSurface.Erase(next, new Point(400, 60), new Point(400, 380), 4) == next);
        session.ChangePage(session.Page with { Items = next });
        var path = Path.GetTempFileName();
        try
        {
            LessonFile.Save(path, session.Lesson);
            Assert(LessonFile.Load(path).Pages[0].Items.Length == next.Length);
        }
        finally { File.Delete(path); }
        session.Undo();
        Assert(session.Page.Items == original, "one undo must restore a whole sweep");
    }

    private static void MarqueeSelection()
    {
        var first = new Ink(Guid.NewGuid(), Visibility.Both, false, InkColor, 6, false, false,
            [new(100, 100), new(130, 100)]);
        var second = first with { Id = Guid.NewGuid(), Points = [new(100, 150), new(130, 150)], Visibility = Visibility.TeacherOnly };
        var student = first with { Id = Guid.NewGuid(), Visibility = Visibility.StudentOnly };
        var session = new Session();
        session.ChangePage(session.Page with { Items = [first, second, student] });
        Assert(BoardSelection.BeginSelected([], new Point(110, 100), 1) is null);
        var group = BoardSelection.Within(session.Page.Items, new Point(140, 165), new Point(90, 90), 1);
        Assert(group.SequenceEqual([first, second]));
        var drag = BoardSelection.BeginSelected(group, new Point(115, 125), 1)!;
        Assert(drag.Cursor == Cursors.SizeAll);
        var moved = drag.CommitAll(new Point(205, 165));
        Assert(BoardSelection.BoundsOf(moved).Location == BoardSelection.BoundsOf(group).Location + new Vector(90, 40));
        var live = session.ProjectMoves(moved)!;
        Assert(live.Items.SequenceEqual([moved[0], session.Output!.Items[1]]));
        session.ToggleFreeze();
        Assert(ReferenceEquals(session.ProjectMoves(moved), session.Output));
    }
}
