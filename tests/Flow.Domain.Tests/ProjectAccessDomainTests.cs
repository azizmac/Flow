using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>Участие в проекте и потолок роли (docs/TZ_project_access.md, этап 4A).</summary>
public class ProjectAccessDomainTests
{
    [Theory]
    [InlineData(UserRole.Reader, ProjectRole.Viewer)]
    [InlineData(UserRole.Member, ProjectRole.Member)]
    [InlineData(UserRole.Developer, ProjectRole.Developer)]
    [InlineData(UserRole.Admin, ProjectRole.Admin)]
    [InlineData(UserRole.Owner, ProjectRole.Admin)]
    public void ToProjectRole_Should_MirrorLadder(UserRole global, ProjectRole expected) =>
        Assert.Equal(expected, global.ToProjectRole());

    [Fact]
    public void BoardMember_Create_Should_SetFields()
    {
        var (board, user, by) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var member = BoardMember.Create(board, user, ProjectRole.Developer, by);

        Assert.Equal((board, user, ProjectRole.Developer, by), (member.BoardId, member.UserId, member.Role, member.AddedById));
    }

    [Fact]
    public void BoardMember_Create_Should_RejectEmptyIdsAndUnknownRole()
    {
        Assert.Throws<ArgumentException>(() => BoardMember.Create(Guid.Empty, Guid.NewGuid(), ProjectRole.Member, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => BoardMember.Create(Guid.NewGuid(), Guid.Empty, ProjectRole.Member, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => BoardMember.Create(Guid.NewGuid(), Guid.NewGuid(), ProjectRole.Member, Guid.Empty));
        Assert.Throws<ArgumentException>(() => BoardMember.Create(Guid.NewGuid(), Guid.NewGuid(), (ProjectRole)9, Guid.NewGuid()));
    }

    [Fact]
    public void BoardMember_ChangeRole_Should_ReportChange()
    {
        var member = BoardMember.Create(Guid.NewGuid(), Guid.NewGuid(), ProjectRole.Member, Guid.NewGuid());

        Assert.False(member.ChangeRole(ProjectRole.Member));
        Assert.True(member.ChangeRole(ProjectRole.Admin));
        Assert.Equal(ProjectRole.Admin, member.Role);
    }

    [Theory]
    [InlineData(ProjectRole.Viewer)]
    [InlineData(ProjectRole.Member)]
    [InlineData(ProjectRole.Developer)]
    public void SetDefaultRole_Should_AcceptBelowAdmin_And_Clear(ProjectRole role)
    {
        var board = Board.Create("Flow", "FLW");
        Assert.Null(board.DefaultRole);

        board.SetDefaultRole(role);
        Assert.Equal(role, board.DefaultRole);

        board.SetDefaultRole(null);
        Assert.Null(board.DefaultRole);
    }

    [Fact]
    public void SetDefaultRole_Should_RejectAdminAndUnknown()
    {
        var board = Board.Create("Flow", "FLW");

        Assert.Throws<ArgumentException>(() => board.SetDefaultRole(ProjectRole.Admin));
        Assert.Throws<ArgumentException>(() => board.SetDefaultRole((ProjectRole)9));
    }
}
