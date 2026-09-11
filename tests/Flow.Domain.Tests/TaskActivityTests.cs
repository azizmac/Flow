using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

public class TaskActivityTests
{
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public void Created_Should_HaveNoValues()
    {
        var activity = TaskActivity.Created(TaskId, ActorId);

        Assert.Equal(TaskActivityType.Created, activity.Type);
        Assert.Null(activity.OldValue);
        Assert.Null(activity.NewValue);
        Assert.Equal(TaskId, activity.TaskId);
        Assert.Equal(ActorId, activity.ActorId);
        Assert.NotEqual(Guid.Empty, activity.Id);
    }

    [Fact]
    public void TitleChanged_Should_KeepBothTitles()
    {
        var activity = TaskActivity.TitleChanged(TaskId, ActorId, "Old", "New");

        Assert.Equal(TaskActivityType.TitleChanged, activity.Type);
        Assert.Equal("Old", activity.OldValue);
        Assert.Equal("New", activity.NewValue);
    }

    [Fact]
    public void StatusChanged_Should_StoreGuidsAsStrings()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();

        var activity = TaskActivity.StatusChanged(TaskId, ActorId, from, to);

        Assert.Equal(from.ToString(), activity.OldValue);
        Assert.Equal(to.ToString(), activity.NewValue);
    }

    [Fact]
    public void AssigneeChanged_Should_StoreNull_For_Unassigned()
    {
        var user = Guid.NewGuid();

        var assigned = TaskActivity.AssigneeChanged(TaskId, ActorId, null, user);
        var unassigned = TaskActivity.AssigneeChanged(TaskId, ActorId, user, null);

        Assert.Null(assigned.OldValue);
        Assert.Equal(user.ToString(), assigned.NewValue);
        Assert.Equal(user.ToString(), unassigned.OldValue);
        Assert.Null(unassigned.NewValue);
    }

    [Fact]
    public void DueDateChanged_Should_StoreIsoDate()
    {
        var activity = TaskActivity.DueDateChanged(TaskId, ActorId, null, new DateOnly(2026, 9, 15));

        Assert.Equal(TaskActivityType.DueDateChanged, activity.Type);
        Assert.Null(activity.OldValue);
        Assert.Equal("2026-09-15", activity.NewValue);
    }

    [Fact]
    public void Comment_Activities_Should_ReferenceCommentId()
    {
        var commentId = Guid.NewGuid();

        var added = TaskActivity.CommentAdded(TaskId, ActorId, commentId);
        var deleted = TaskActivity.CommentDeleted(TaskId, ActorId, commentId);

        Assert.Equal(commentId.ToString(), added.NewValue);
        Assert.Null(added.OldValue);
        Assert.Equal(commentId.ToString(), deleted.OldValue);
        Assert.Null(deleted.NewValue);
    }

    [Fact]
    public void Factories_Should_Throw_On_EmptyIds()
    {
        Assert.Throws<ArgumentException>(() => TaskActivity.Created(Guid.Empty, ActorId));
        Assert.Throws<ArgumentException>(() => TaskActivity.Created(TaskId, Guid.Empty));
    }
}
