using System.Collections.Immutable;

namespace EduCast.Classroom;

// Frames contain only renderable, approved content. A consumer never receives a Lesson or Page.
public sealed class ProgramFrame
{
    private ProgramFrame(BoardPaper paper, uint? background, ImmutableArray<Content> items) =>
        (Paper, Background, Items) = (paper, background, items);

    public BoardPaper Paper { get; }
    public uint? Background { get; }
    public ImmutableArray<Content> Items { get; }

    internal static bool IsApproved(Page page, Content item) =>
        (item.Visibility is Visibility.Both or Visibility.StudentOnly) &&
        (!item.IsAnswer || page.AnswersRevealed);

    public static ProgramFrame From(Page page) => new(page.Paper, page.Background, page.Items
        .Where(item => IsApproved(page, item))
        .Select(item => item.Visibility == Visibility.Both && !item.IsAnswer
            ? item : item with { Visibility = Visibility.Both, IsAnswer = false })
        .ToImmutableArray());

    internal ProgramFrame WithGesture(Content item, Content? original)
    {
        var projected = item.Visibility == Visibility.Both && !item.IsAnswer
            ? item : item with { Visibility = Visibility.Both, IsAnswer = false };
        if (original is null)
            return new ProgramFrame(Paper, Background, Items.Add(projected));
        for (var i = 0; i < Items.Length; i++)
        {
            if (Items[i].Id == original.Id)
                return new ProgramFrame(Paper, Background, Items.SetItem(i, projected));
        }
        return this;
    }

    internal ProgramFrame Without(HashSet<Guid> ids) => Items.Any(item => ids.Contains(item.Id))
        ? new ProgramFrame(Paper, Background, Items.Where(item => !ids.Contains(item.Id)).ToImmutableArray())
        : this;
}

public sealed class ProgramInk
{
    internal ProgramInk(Ink stroke) => Stroke = stroke;
    public Ink Stroke { get; }
}

public sealed class Session
{
    private readonly List<(Lesson Lesson, int Index)> undo = [];
    private readonly List<(Lesson Lesson, int Index)> redo = [];
    private ProgramFrame? held;
    private ProgramFrame? program;
    private ProgramFrame? lastProjection;
    private bool live = true;

    public Session() => Reset(Lesson.Empty());

    public Lesson Lesson { get; private set; } = null!;
    public int PageIndex { get; private set; }
    public Page Page => Lesson.Pages[PageIndex];
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public bool Frozen => held is not null;
    public bool Blackout { get; set; }
    public ProgramFrame Preview => lastProjection!;
    public ProgramFrame? Output => Blackout ? null : held ?? program;

    public ProgramFrame? ProjectGesture(Content? item, Content? original = null)
    {
        var frame = Output;
        if (item is null || frame is null || !Live || Frozen || !ProgramFrame.IsApproved(Page, item))
            return frame;
        if (original is not null && (original.Id != item.Id || !Page.Items.Contains(original) ||
            !ProgramFrame.IsApproved(Page, original)))
            return frame;
        return frame.WithGesture(item, original);
    }

    public ProgramInk? ProjectLiveInk(Ink? stroke) =>
        stroke is not null && !stroke.Points.IsDefaultOrEmpty && Live && !Frozen && !Blackout &&
        ProgramFrame.IsApproved(Page, stroke)
            ? new ProgramInk(stroke with { Visibility = Visibility.Both, IsAnswer = false })
            : null;

    public bool Live
    {
        get => live;
        set
        {
            live = value;
            if (value)
                Take();
        }
    }

    public void Take() => program = Preview;

    public void ToggleFreeze()
    {
        if (held is not null)
            held = null;
        else
            held = program;
    }

    public void Reset(Lesson lesson)
    {
        undo.Clear();
        redo.Clear();
        PageIndex = 0;
        Lesson = lesson;
        lastProjection = ProgramFrame.From(Page);
        held = null;
        Blackout = false;
        live = true;
        Take();
    }

    public void GoTo(int index)
    {
        if (index < 0 || index >= Lesson.Pages.Length || index == PageIndex)
            return;
        var previous = Page;
        PageIndex = index;
        Refresh(previous);
    }

    public void AddPage() => Commit(Lesson with { Pages = Lesson.Pages.Add(Page.Empty() with { Subject = Page.Subject }) },
        Lesson.Pages.Length);

    public void DuplicatePage()
    {
        var copy = Page with
        {
            Items = Page.Items.Select(item => item with { Id = Guid.NewGuid() }).ToImmutableArray()
        };
        var index = PageIndex + 1;
        Commit(Lesson with { Pages = Lesson.Pages.Insert(index, copy) }, index);
    }

    public void ChangePage(Page page) =>
        Commit(Lesson with { Pages = Lesson.Pages.SetItem(PageIndex, page) }, PageIndex);

    public void Rename(string title) => Commit(Lesson with { Title = title }, PageIndex);

    public void Undo()
    {
        if (!CanUndo)
            return;
        Restore(undo, redo);
    }

    public void Redo()
    {
        if (!CanRedo)
            return;
        Restore(redo, undo);
    }

    private void Restore(List<(Lesson Lesson, int Index)> from, List<(Lesson Lesson, int Index)> to)
    {
        var previous = Page;
        to.Add((Lesson, PageIndex));
        (Lesson, PageIndex) = from[^1];
        from.RemoveAt(from.Count - 1);
        Refresh(previous);
    }

    private void Commit(Lesson lesson, int index)
    {
        if (Lesson == lesson)
            return;
        var previous = Page;
        undo.Add((Lesson, PageIndex));
        if (undo.Count > 100)
            undo.RemoveAt(0);
        redo.Clear();
        Lesson = lesson;
        PageIndex = index;
        Refresh(previous);
    }

    private void Refresh(Page previous)
    {
        if (previous.Paper == Page.Paper && previous.Background == Page.Background && previous.Items == Page.Items &&
            previous.AnswersRevealed == Page.AnswersRevealed)
            return;
        // Revocation applies immediately, even to content already taken or held.
        // Other draft edits still respect manual take and freeze.
        var revoked = Page.Items.Where(item => !ProgramFrame.IsApproved(Page, item))
            .Select(item => item.Id).ToHashSet();
        if (revoked.Count > 0)
        {
            program = program?.Without(revoked);
            held = held?.Without(revoked);
        }
        lastProjection = ProgramFrame.From(Page);
        if (Live)
            Take();
    }
}
