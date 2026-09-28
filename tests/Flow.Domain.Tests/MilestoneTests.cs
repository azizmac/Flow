using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>Веха (docs/TZ_task_views.md §6): имя, закрытие и повторное открытие, задачи в закрытой вехе.</summary>
public class MilestoneTests
{
    [Fact]
    public void Name_Is_Required_And_Trimmed_Description_Empty_Is_Null()
    {
        Assert.Throws<ArgumentException>(() => Milestone.Create(Guid.NewGuid(), "  ", null, null, 0));
        Assert.Throws<ArgumentException>(() => Milestone.Create(Guid.NewGuid(), new string('x', Milestone.NameMaxLength + 1), null, null, 0));

        var milestone = Milestone.Create(Guid.NewGuid(), "  Релиз 1.0 ", "   ", null, 0);

        Assert.Equal(("Релиз 1.0", null), (milestone.Name, milestone.Description));
        Assert.Equal(MilestoneState.Open, milestone.State);
    }

    [Fact]
    public void Close_And_Reopen_Only_From_The_Other_State()
    {
        var milestone = Milestone.Create(Guid.NewGuid(), "1.0", null, null, 0);
        var now = DateTime.UtcNow;

        Assert.Throws<InvalidOperationException>(milestone.Reopen);
        milestone.Close(now);
        Assert.Equal((MilestoneState.Closed, now), (milestone.State, milestone.ClosedAt));
        Assert.Throws<InvalidOperationException>(() => milestone.Close(now));
        milestone.Reopen();
        Assert.Null(milestone.ClosedAt);
    }

    [Fact]
    public void Closed_Milestone_Keeps_Its_Tasks_But_Takes_No_New_Ones()
    {
        var board = Board.Create("Проект", "PRJ");
        var milestone = Milestone.Create(board.Id, "1.0", null, null, 0);
        var inside = board.CreateTask("Внутри");
        var outside = board.CreateTask("Снаружи");
        inside.SetMilestone(milestone);
        milestone.Close(DateTime.UtcNow);

        inside.SetMilestone(milestone);
        Assert.Throws<InvalidOperationException>(() => outside.SetMilestone(milestone));
        Assert.Throws<InvalidOperationException>(() => outside.SetMilestone(Milestone.Create(Guid.NewGuid(), "Чужая", null, null, 0)));
        inside.SetMilestone(null);
        Assert.Null(inside.MilestoneId);
    }

    [Fact]
    public void Shared_Milestone_Accepts_Tasks_Of_Its_Projects_Only()
    {
        var front = Board.Create("Фронт", "FRONT");
        var back = Board.Create("Бэк", "BACK");
        var milestone = Milestone.Create(front.Id, "2.0", null, null, 0);
        var backTask = back.CreateTask("API");
        Assert.Throws<InvalidOperationException>(() => backTask.SetMilestone(milestone));

        milestone.ShareWith([back.Id, front.Id, back.Id, Guid.Empty]);
        Assert.Equal([back.Id], milestone.SharedBoardIds);
        Assert.True(milestone.IsAvailableIn(front.Id) && milestone.IsAvailableIn(back.Id));
        backTask.SetMilestone(milestone);
        Assert.Equal(milestone.Id, backTask.MilestoneId);

        Assert.Throws<ArgumentException>(() => milestone.ShareWith(Enumerable.Range(0, Milestone.MaxSharedBoards + 1).Select(_ => Guid.NewGuid())));
    }
}
