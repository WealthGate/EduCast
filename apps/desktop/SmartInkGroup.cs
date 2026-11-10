using System.Collections.Immutable;
using System.Windows.Threading;
using EduCast.Classroom;

namespace EduCast.Studio;

internal sealed class SmartInkGroup
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background);
    private readonly List<Ink> strokes = [];
    private readonly Func<ImmutableArray<Content>> items;
    private readonly Action<ImmutableArray<Content>> replace;
    private BoardSubject subject;
    private bool math;

    internal SmartInkGroup(Func<ImmutableArray<Content>> items, Action<ImmutableArray<Content>> replace)
    {
        (this.items, this.replace) = (items, replace);
        timer.Tick += (_, _) => Complete();
    }

    internal void Pause() => timer.Stop();

    internal void Resume()
    {
        if (strokes.Count > 0)
            timer.Start();
    }

    internal void Add(Ink ink, bool mathMode, BoardSubject boardSubject)
    {
        timer.Stop();
        var current = items();
        if (current.IsEmpty || !ReferenceEquals(current[^1], ink))
        {
            Clear();
            return;
        }
        if (math != mathMode || !EndsWith(current, 1) ||
            strokes.Count > 0 && !SameInkStyle(strokes[^1], ink))
            strokes.Clear();
        strokes.Add(ink);
        if (strokes.Count > (mathMode ? 64 : 3))
            strokes.RemoveAt(0);
        (math, subject) = (mathMode, boardSubject);
        timer.Interval = TimeSpan.FromMilliseconds(math ? 1000 : 550);
        timer.Start();
    }

    internal void Complete()
    {
        timer.Stop();
        var current = items();
        if (strokes.Count == 0 || !EndsWith(current, 0))
        {
            Clear();
            return;
        }

        (Content Item, int Count)? recognized = math ? RecognizeMath() :
            SketchRecognition.RecognizeGroup(strokes, subject) is { } shape ? (shape.Shape, shape.Count) : null;
        strokes.Clear();
        if (recognized is { } match)
            replace(current.RemoveRange(current.Length - match.Count, match.Count).Add(match.Item));
    }

    internal void Clear()
    {
        timer.Stop();
        strokes.Clear();
    }

    private (Content, int)? RecognizeMath()
    {
        if (MathInkRecognition.Recognize(strokes) is not { } found || found.Math.Source.Contains('?'))
            return null;
        var mark = found.Math;
        var style = strokes[^1];
        var origin = new BoardPoint(mark.Bounds.Left, mark.Bounds.Top);
        var size = Math.Clamp(mark.Bounds.Height * .74, 24, 72);
        if (mark.IsSymbol)
            return (new BoardText(style.Id, style.Visibility, style.IsAnswer, style.Argb, origin, size, mark.Source), found.Count);
        return MathParser.TryParse(mark.Source, out _, out _)
            ? (new Equation(style.Id, style.Visibility, style.IsAnswer, style.Argb, origin, size, mark.Source), found.Count)
            : null;
    }

    private bool EndsWith(ImmutableArray<Content> current, int offset)
    {
        var start = current.Length - strokes.Count - offset;
        return start >= 0 && Enumerable.Range(0, strokes.Count).All(i => ReferenceEquals(current[start + i], strokes[i]));
    }

    private static bool SameInkStyle(Ink first, Ink last) => first.Visibility == last.Visibility &&
        first.IsAnswer == last.IsAnswer && first.Argb == last.Argb && first.Width == last.Width;
}
