using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

public class TaskCommentTests
{
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid AuthorId = Guid.NewGuid();

    [Fact]
    public void Create_Should_TrimBody_And_DeduplicateMentions()
    {
        var mentioned = Guid.NewGuid();

        var comment = TaskComment.Create(TaskId, AuthorId, "  Привет, @ilya  ", [mentioned, mentioned]);

        Assert.Equal("Привет, @ilya", comment.Body);
        Assert.Equal(TaskId, comment.TaskId);
        Assert.Equal(AuthorId, comment.AuthorId);
        Assert.Null(comment.EditedAt);
        var mention = Assert.Single(comment.Mentions);
        Assert.Equal(mentioned, mention.UserId);
        Assert.Equal(comment.Id, mention.CommentId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    public void Create_Should_Throw_When_BodyIsEmpty(string body)
    {
        Assert.Throws<ArgumentException>(() => TaskComment.Create(TaskId, AuthorId, body, []));
    }

    [Fact]
    public void Create_Should_Throw_When_BodyTooLong()
    {
        var body = new string('a', TaskComment.BodyMaxLength + 1);

        Assert.Throws<ArgumentException>(() => TaskComment.Create(TaskId, AuthorId, body, []));
    }

    [Fact]
    public void Create_Should_Throw_When_EmptyIds()
    {
        Assert.Throws<ArgumentException>(() => TaskComment.Create(Guid.Empty, AuthorId, "x", []));
        Assert.Throws<ArgumentException>(() => TaskComment.Create(TaskId, Guid.Empty, "x", []));
        Assert.Throws<ArgumentException>(() => TaskComment.Create(TaskId, AuthorId, "x", [Guid.Empty]));
    }

    [Fact]
    public void Edit_Should_ReplaceBodyAndMentions_And_SetEditedAt()
    {
        var comment = TaskComment.Create(TaskId, AuthorId, "old", [Guid.NewGuid()]);
        var newMention = Guid.NewGuid();

        var changed = comment.Edit("new @user", [newMention]);

        Assert.True(changed);
        Assert.Equal("new @user", comment.Body);
        Assert.NotNull(comment.EditedAt);
        Assert.Equal(newMention, Assert.Single(comment.Mentions).UserId);
    }

    [Fact]
    public void Edit_Should_BeNoop_When_BodyUnchanged()
    {
        var comment = TaskComment.Create(TaskId, AuthorId, "same", []);

        var changed = comment.Edit("  same ", [Guid.NewGuid()]);

        Assert.False(changed);
        Assert.Null(comment.EditedAt);
        Assert.Empty(comment.Mentions);
    }
}
