using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>Спринт (docs/TZ_task_views.md §2): жизненный цикл, снимки, правка завершённого, перенос задач.</summary>
public class SprintTests
{
    private static readonly DateOnly Start = new(2026, 10, 1);

    [Fact]
    public void Start_Requires_Planned_State_And_Valid_Dates_And_Takes_Snapshot()
    {
        var board = Board.Create("Проект", "PRJ");
        var sprint = Sprint.Create(board.Id, "Спринт 1", null, 0);
        var task = board.CreateTask("Задача");
        task.SetStoryPoints(5m);
        task.SetSprint(sprint);

        Assert.Throws<ArgumentException>(() => sprint.Start(Start, Start, [task], DateTime.UtcNow));
        sprint.Start(Start, Start.AddDays(14), [task], DateTime.UtcNow);

        Assert.Equal(SprintState.Active, sprint.State);
        var commitment = Assert.Single(sprint.Commitments);
        Assert.Equal((task.Id, SprintCommitmentKind.Committed, 5m), (commitment.TaskId, commitment.Kind, commitment.StoryPoints));
        Assert.Throws<InvalidOperationException>(() => sprint.Start(Start, Start.AddDays(14), [], DateTime.UtcNow));
        Assert.Throws<InvalidOperationException>(() => sprint.SetDates(null, null));
    }

    [Fact]
    public void Complete_Moves_Open_Tasks_And_Keeps_Closed_Ones()
    {
        var board = Board.Create("Проект", "PRJ");
        var sprint = Sprint.Create(board.Id, "Спринт 1", null, 0);
        var next = Sprint.Create(board.Id, "Спринт 2", null, 1);
        var open = board.CreateTask("Открытая");
        var closed = board.CreateTask("Закрытая");
        open.SetSprint(sprint);
        closed.SetSprint(sprint);
        sprint.Start(Start, Start.AddDays(7), [open, closed], DateTime.UtcNow);

        var moved = sprint.Complete([open], next, DateTime.UtcNow);

        Assert.Equal([open], moved);
        Assert.Equal(next.Id, open.SprintId);
        Assert.Equal(sprint.Id, closed.SprintId);
        Assert.Contains(sprint.Commitments, c => c.TaskId == open.Id && c.Kind == SprintCommitmentKind.CarriedOver);
        Assert.True(sprint.IsCompleted);
    }

    [Fact]
    public void Completed_Sprint_Only_Renames_And_Takes_No_New_Tasks()
    {
        var board = Board.Create("Проект", "PRJ");
        var sprint = Sprint.Create(board.Id, "Спринт 1", null, 0);
        sprint.Start(Start, Start.AddDays(7), [], DateTime.UtcNow);
        sprint.Complete([], null, DateTime.UtcNow);

        sprint.Rename("Релизный");
        Assert.Equal("Релизный", sprint.Name);
        Assert.Throws<InvalidOperationException>(() => sprint.SetGoal("цель"));
        Assert.Throws<InvalidOperationException>(() => board.CreateTask("Новая").SetSprint(sprint));
    }

    [Fact]
    public void Task_Takes_Only_Sprint_Of_Its_Project_And_Names_Are_Validated()
    {
        var board = Board.Create("Проект", "PRJ");
        var other = Sprint.Create(Board.Create("Другой", "OTH").Id, "Спринт", null, 0);

        Assert.Throws<InvalidOperationException>(() => board.CreateTask("Задача").SetSprint(other));
        Assert.Throws<ArgumentException>(() => Sprint.Create(board.Id, " ", null, 0));
        Assert.Throws<ArgumentException>(() => Sprint.Create(board.Id, new string('x', Sprint.NameMaxLength + 1), null, 0));
        Assert.Throws<ArgumentException>(() => Sprint.Create(board.Id, "Спринт", null, 0, Start, Start));
    }
}
