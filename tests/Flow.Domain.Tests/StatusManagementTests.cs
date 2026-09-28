using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>Управление статусами проекта (docs/TZ_workflow_config.md §1, этап 3A).</summary>
public class StatusManagementTests
{
    private static Board NewBoard() => Board.Create("Flow", "FLW");

    private static Status Named(Board board, string name) => board.Statuses.Single(s => s.Name == name);

    [Fact]
    public void AddStatus_Should_Append_And_Reject_Duplicate_Name_Ignoring_Case()
    {
        var board = NewBoard();
        var max = board.Statuses.Max(s => s.SortOrder);

        var added = board.AddStatus("  Тестирование  ", StatusType.InReview);

        Assert.Equal("Тестирование", added.Name);
        Assert.Equal(max + 1, added.SortOrder);
        Assert.Throws<InvalidOperationException>(() => board.AddStatus("в работе"));
    }

    [Fact]
    public void AddStatus_Should_Reject_Unknown_Type() =>
        Assert.Throws<ArgumentException>(() => NewBoard().AddStatus("X", (StatusType)42));

    [Fact]
    public void RenameStatus_Should_Rename_And_Reject_Taken_Or_Empty_Name()
    {
        var board = NewBoard();
        var review = Named(board, "На проверке");

        board.RenameStatus(review.Id, "Ревью");

        Assert.Equal("Ревью", review.Name);
        Assert.Throws<InvalidOperationException>(() => board.RenameStatus(review.Id, "СДЕЛАНА"));
        Assert.Throws<ArgumentException>(() => board.RenameStatus(review.Id, "  "));
        Assert.Throws<ArgumentException>(() => board.RenameStatus(review.Id, new string('x', Status.NameMaxLength + 1)));
    }

    [Fact]
    public void SetStatusFinal_Should_Keep_At_Least_One_Final_And_Never_Initial()
    {
        var board = NewBoard();
        var done = Named(board, "Сделана");
        var review = Named(board, "На проверке");

        Assert.Throws<InvalidOperationException>(() => board.SetStatusFinal(done.Id, false));
        Assert.Throws<InvalidOperationException>(() => board.SetStatusFinal(Named(board, "Не начата").Id, true));

        board.SetStatusFinal(review.Id, true);
        board.SetStatusFinal(done.Id, false);

        Assert.Equal(review.Id, Assert.Single(board.Statuses, s => s.IsFinal).Id);
    }

    [Fact]
    public void SetInitialStatus_Should_Reject_Final_Status()
    {
        var board = NewBoard();

        Assert.Throws<InvalidOperationException>(() => board.SetInitialStatus(Named(board, "Сделана").Id));
    }

    [Fact]
    public void SetStatusType_Should_Set_Clear_And_Reject_Unknown()
    {
        var board = NewBoard();
        var review = Named(board, "На проверке");

        board.SetStatusType(review.Id, null);
        Assert.Null(review.Type);

        board.SetStatusType(review.Id, StatusType.InProgress);
        Assert.Equal(StatusType.InProgress, review.Type);

        Assert.Throws<ArgumentException>(() => board.SetStatusType(review.Id, (StatusType)42));
    }

    [Fact]
    public void ReorderStatuses_Should_Follow_Given_Order_Above_Old_Values()
    {
        var board = NewBoard();
        var oldMax = board.Statuses.Max(s => s.SortOrder);
        var reversed = board.Statuses.OrderByDescending(s => s.SortOrder).Select(s => s.Id).ToList();

        board.ReorderStatuses(reversed);

        Assert.Equal(reversed, board.Statuses.OrderBy(s => s.SortOrder).Select(s => s.Id));
        // Новые значения выше старых: построчная проверка unique (BoardId, SortOrder) не встретит дубль.
        Assert.All(board.Statuses, s => Assert.True(s.SortOrder > oldMax));
    }

    [Fact]
    public void ReorderStatuses_Should_Require_Full_Permutation()
    {
        var board = NewBoard();
        var ids = board.Statuses.Select(s => s.Id).ToList();

        Assert.Throws<ArgumentException>(() => board.ReorderStatuses(ids.Skip(1).ToList()));
        Assert.Throws<ArgumentException>(() => board.ReorderStatuses([.. ids.Skip(1), ids[1]]));
        Assert.Throws<ArgumentException>(() => board.ReorderStatuses([.. ids.Skip(1), Guid.NewGuid()]));
    }

    [Fact]
    public void RemoveStatus_Should_Protect_Initial_And_Last_Final()
    {
        var board = NewBoard();
        var initial = Named(board, "Не начата");
        var done = Named(board, "Сделана");
        var review = Named(board, "На проверке");

        Assert.Throws<InvalidOperationException>(() => board.RemoveStatus(initial.Id, review.Id));
        Assert.Throws<InvalidOperationException>(() => board.RemoveStatus(done.Id, review.Id));
        Assert.Throws<InvalidOperationException>(() => board.RemoveStatus(review.Id, review.Id));
        Assert.Throws<InvalidOperationException>(() => board.RemoveStatus(review.Id, Guid.NewGuid()));

        board.RemoveStatus(review.Id, initial.Id);
        Assert.DoesNotContain(board.Statuses, s => s.Id == review.Id);

        // Второй финальный — и первый уже можно удалить.
        var cancelled = board.AddStatus("Отменена", isFinal: true);
        board.RemoveStatus(done.Id, cancelled.Id);
        Assert.Equal(cancelled.Id, Assert.Single(board.Statuses, s => s.IsFinal).Id);
    }
}
