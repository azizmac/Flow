using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>Настройки канбана в домене (этап 2B): WIP-лимит статуса, окно финальной колонки, представление по умолчанию.</summary>
public class KanbanTests
{
    [Fact]
    public void Wip_Limit_Is_Optional_And_Bounded()
    {
        var board = Board.Create("Канбан", "KBN");
        var status = board.Statuses.First();

        board.SetStatusWipLimit(status.Id, 5);
        Assert.Equal(5, status.WipLimit);
        board.SetStatusWipLimit(status.Id, null);
        Assert.Null(status.WipLimit);

        Assert.Throws<ArgumentException>(() => board.SetStatusWipLimit(status.Id, 0));
        Assert.Throws<ArgumentException>(() => board.SetStatusWipLimit(status.Id, Status.MaxWipLimit + 1));
    }

    [Fact]
    public void Done_Column_Window_Defaults_To_Two_Weeks_And_Is_Bounded()
    {
        var board = Board.Create("Канбан", "KBN");
        Assert.Equal(14, board.DoneColumnDays);

        board.SetDoneColumnDays(30);
        Assert.Equal(30, board.DoneColumnDays);
        Assert.Throws<ArgumentException>(() => board.SetDoneColumnDays(0));
        Assert.Throws<ArgumentException>(() => board.SetDoneColumnDays(Board.MaxDoneColumnDays + 1));
    }

    [Fact]
    public void New_Task_Status_Change_Time_Starts_At_Creation()
    {
        var board = Board.Create("Канбан", "KBN");
        var task = board.CreateTask("Задача");

        Assert.Equal(task.CreatedAt, task.StatusChangedAt);
    }

    [Fact]
    public void Tasks_View_Is_Part_Of_Preferences()
    {
        var preferences = UserPreferences.Default.With(tasksView: TaskView.Board);

        Assert.Equal(TaskView.List, UserPreferences.Default.TasksView);
        Assert.Equal(TaskView.Board, preferences.TasksView);
        Assert.False(preferences.SameAs(UserPreferences.Default));
        Assert.Throws<ArgumentException>(() => UserPreferences.Default.With(tasksView: (TaskView)42));
    }
}
