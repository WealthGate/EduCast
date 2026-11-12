using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using EduCast.Classroom;

namespace EduCast.Studio;

public partial class TeacherWindow
{
    private readonly CanvasTextEditor textEditor;
    private readonly DispatcherTimer animation = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private double programMotionTime;

    internal BoardSubject CurrentSubject => session.Page.Subject;

    internal void BeginBoardText(Point position)
    {
        var existing = BoardSelection.Hit(session.Page.Items, position) as BoardText;
        EditBoardText(existing ?? CanvasTextEditor.New(position, InkColor, newVisibility, newAnswer));
    }

    private void EditBoardText(BoardText text)
    {
        textEditor.Open(text, next =>
        {
            if (session.Page.Items.FirstOrDefault(item => item.Id == text.Id) is BoardText current)
            {
                next = current with { Text = next.Text, Argb = next.Argb, Size = next.Size, Style = next.Style };
                if (next != current)
                    session.ChangePage(session.Page with { Items = session.Page.Items.Replace(current, next) });
                Refresh();
            }
            else
                AddItem(next);
        });
    }

    private void InsertEffect(object sender, RoutedEventArgs args)
    {
        textEditor.Commit();
        var library = new EffectLibrary(this, CurrentSubject);
        if (library.ShowDialog() == true && library.Result is { } effect)
            BeginPlacement(origin => effect.Place(origin, newVisibility, newAnswer));
    }

    internal void RefreshAnimation()
    {
        if (!ready)
            return;
        var time = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        var programActive = !session.Blackout && !session.Frozen && ProgramPreview.HasMotion;
        if (programActive)
        {
            programMotionTime = time;
            ProgramPreview.SetMotionTime(time);
            output?.SetMotionTime(time);
            broadcastOutput?.SetMotionTime(time);
        }
        var sourceActive = sourceWindow?.HasMotion == true;
        if (sourceActive)
            sourceWindow!.SetMotionTime(time);
        if (TeacherBoard.HasMotion)
            TeacherBoard.SetMotionTime(time);
        animation.IsEnabled = programActive || sourceActive || TeacherBoard.HasMotion;
    }
}
