using System.Windows;
using System.Windows.Input;
using EduCast.Classroom;
using EduCast.Studio;
using Visibility = EduCast.Classroom.Visibility;

internal static partial class Checks
{
    private static void SelectionResize()
    {
        var ink = new Ink(Guid.NewGuid(), Visibility.Both, false, InkColor, 8, false, false,
            [new(120, 150, .2f), new(220, 150, .8f)]);
        Content[] items =
        [
            ink,
            new Shape(Guid.NewGuid(), Visibility.Both, false, InkColor, 6, ShapeKind.Arrow, new(120, 150), new(220, 180)),
            new Shape(Guid.NewGuid(), Visibility.Both, false, InkColor, 6, ShapeKind.Rectangle, new(120, 150), new(220, 230)),
            new Shape(Guid.NewGuid(), Visibility.Both, false, InkColor, 6, ShapeKind.Circle, new(180, 190), new(220, 190)),
            new BoardText(Guid.NewGuid(), Visibility.TeacherOnly, false, InkColor, new(120, 150), 28, "Resize this text"),
            new Equation(Guid.NewGuid(), Visibility.Both, true, InkColor, new(120, 150), 42, "sqrt(x^2+y^2)"),
            new Graph(Guid.NewGuid(), Visibility.Both, false, InkColor, new(120, 150), "y=x^2-4x+3")
        ];
        foreach (var item in items)
        {
            var bounds = BoardSurface.BoundsOf(item);
            var edge = new Point(bounds.Right + 8, bounds.Top + bounds.Height / 2);
            var drag = BoardSelection.Begin([item], item, edge, 1)!;
            Assert(drag.Cursor == Cursors.SizeWE && ReferenceEquals(drag.Commit(edge), item));
            var end = edge + new Vector(65, 0);
            var draft = Rect.Transform(bounds, drag.Preview(end));
            var result = drag.Commit(end);
            var resized = BoardSurface.BoundsOf(result);
            Assert(result.Id == item.Id && result.Visibility == item.Visibility && result.IsAnswer == item.IsAnswer);
            Assert(result with { Placement = null } == item && result.Placement is { Scale: > 1.1 });
            Assert(Math.Abs(resized.Left - bounds.Left) < .001 && Math.Abs(resized.Width - draft.Width) < .001);
            var shifted = BoardSurface.Move(result, new Vector(16, 12));
            Assert(Math.Abs(BoardSurface.BoundsOf(shifted).Left - resized.Left - 16) < .001);
        }

        var shape = items[2];
        var box = BoardSurface.BoundsOf(shape);
        var corner = new Point(box.Right + 8, box.Bottom + 8);
        var cornerDrag = BoardSelection.Begin([shape], shape, corner, 1)!;
        Assert(cornerDrag.Cursor == Cursors.SizeNWSE);
        var large = cornerDrag.Commit(corner + new Vector(5000, 5000));
        var largeBounds = BoardSurface.BoundsOf(large);
        Assert(largeBounds.Width > box.Width && largeBounds.Right <= BoardSurface.BoardWidth + .001 &&
            largeBounds.Bottom <= BoardSurface.BoardHeight + .001);
        Assert(BoardSurface.Hit([large], new Point(largeBounds.Right - 3, largeBounds.Top + 40)) == large,
            "the scaled shape must be erasable at its new edge");

        var session = new Session();
        session.ChangePage(session.Page with { Items = [shape] });
        session.ChangePage(session.Page with { Items = [large] });
        Assert(session.Output!.Items.Single() == large);
        session.Undo();
        Assert(session.Output!.Items.Single() == shape);
    }

    private static void ObjectGestureProjection()
    {
        var session = new Session();
        var shape = new Shape(Guid.NewGuid(), Visibility.Both, false, InkColor, 4, ShapeKind.Line, new(10, 20), new(50, 20));
        var hidden = shape with { Id = Guid.NewGuid(), Visibility = Visibility.TeacherOnly };
        var answer = shape with { Id = Guid.NewGuid(), IsAnswer = true };
        session.ChangePage(session.Page with { Items = [shape, hidden, answer] });
        var frame = session.Output;
        var moving = shape with { Start = new BoardPoint(80, 20) };
        Assert(session.ProjectGesture(moving, shape)?.Items.Single() == moving);
        Assert(ReferenceEquals(session.Output, frame) && session.Page.Items[0] == shape);
        Assert(ReferenceEquals(session.ProjectGesture(hidden with { Visibility = Visibility.Both }, hidden), frame));
        Assert(ReferenceEquals(session.ProjectGesture(answer, answer), frame));
        session.Live = false;
        Assert(ReferenceEquals(session.ProjectGesture(moving, shape), frame));
        session.Live = true;
        session.ToggleFreeze();
        Assert(ReferenceEquals(session.ProjectGesture(moving, shape), frame));
        session.Blackout = true;
        Assert(session.ProjectGesture(moving, shape) is null);
    }
}
