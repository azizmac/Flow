using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Flow.Application.Features.Users.Commands.UserDeactivateCommand;
using Flow.Application.Features.Users.Commands.UserRemoveLinkCommand;
using Flow.Application.Features.Users.Commands.UserSetLinkCommand;
using Flow.Application.Features.Users.Queries.UserGetQuery;
using Flow.Application.Features.Users.Queries.UserSearchQuery;
using Flow.Shared.Contracts.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>Тесты изолируют данные уникальными username/email, т.к. контейнер общий на коллекцию.</summary>
[Collection(PostgresCollection.Name)]
public class UserPersistenceTests(PostgresFixture db)
{
    private async Task<UserResponse> CreateAsync(string username, string firstName = "Илья", string lastName = "Моторин")
    {
        var result = await db.SendAsync(new UserCreateCommand(username, $"{username}@example.com", firstName, lastName, "correct horse battery"));
        Assert.False(result.IsConflict);
        return result.Response!;
    }

    [Fact]
    public async Task CreateUser_Should_PersistLinks_And_LoadThemBack()
    {
        var user = await CreateAsync("links");
        await db.SendAsync(new UserSetLinkCommand(user.Id, UserLinkType.GitHub, "https://github.com/links"));
        await db.SendAsync(new UserSetLinkCommand(user.Id, UserLinkType.Telegram, "https://t.me/links"));

        var loaded = await db.SendAsync(new UserGetQuery(user.Id));

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Links.Count);
        Assert.Contains(loaded.Links, l => l.Type == UserLinkType.GitHub && l.Url == "https://github.com/links");
        Assert.Equal(2, await db.QueryAsync(ctx => ctx.Users.Where(u => u.Id == user.Id).SelectMany(u => u.Links).CountAsync()));
    }

    [Fact]
    public async Task SetLink_SameType_Should_UpdateRow_NotInsertSecond()
    {
        var user = await CreateAsync("relink");
        await db.SendAsync(new UserSetLinkCommand(user.Id, UserLinkType.GitHub, "https://github.com/old"));

        await db.SendAsync(new UserSetLinkCommand(user.Id, UserLinkType.GitHub, "https://github.com/new"));

        var links = await db.QueryAsync(ctx => ctx.Users.Where(u => u.Id == user.Id).SelectMany(u => u.Links).ToListAsync());
        var link = Assert.Single(links);
        Assert.Equal("https://github.com/new", link.Url);
    }

    [Fact]
    public async Task RemoveLink_Should_DeleteRow()
    {
        var user = await CreateAsync("unlink");
        await db.SendAsync(new UserSetLinkCommand(user.Id, UserLinkType.Website, "https://example.com"));

        await db.SendAsync(new UserRemoveLinkCommand(user.Id, UserLinkType.Website));

        Assert.Equal(0, await db.QueryAsync(ctx => ctx.Users.Where(u => u.Id == user.Id).SelectMany(u => u.Links).CountAsync()));
    }

    [Fact]
    public async Task CreateUser_Should_ReturnConflict_When_UsernameDiffersOnlyByCase()
    {
        // Регрессия на будущее: дубликат не должен долетать до IX_Users_Username и превращаться в 500.
        await CreateAsync("dupuser");

        var second = await db.SendAsync(new UserCreateCommand("DupUser", "another@example.com", "A", "B", "correct horse battery"));

        Assert.True(second.IsUsernameTaken);
        Assert.Equal(1, await db.QueryAsync(ctx => ctx.Users.CountAsync(u => u.Username == "dupuser")));
    }

    [Fact]
    public async Task CreateUser_Should_ReturnConflict_When_EmailTaken()
    {
        await CreateAsync("dupmail");

        var second = await db.SendAsync(new UserCreateCommand("dupmail2", "DupMail@Example.com", "A", "B", "correct horse battery"));

        Assert.True(second.IsEmailTaken);
    }

    [Fact]
    public async Task UniqueIndexes_Should_ExistInDatabase()
    {
        // Страховка на случай, если проверка в Application будет обойдена: индексы должны быть в схеме.
        var indexes = await db.QueryAsync(ctx => ctx.Database
            .SqlQueryRaw<string>("SELECT indexname AS \"Value\" FROM pg_indexes WHERE tablename = 'Users'")
            .ToListAsync());

        Assert.Contains("IX_Users_Username", indexes);
        Assert.Contains("IX_Users_Email", indexes);
    }

    [Fact]
    public async Task Search_Should_MatchCaseInsensitive_And_SkipInactive()
    {
        await CreateAsync("search.active", "Пётр", "Поисков");
        var inactive = await CreateAsync("search.inactive", "Пётр", "Поисков");
        await db.SendAsync(new UserDeactivateCommand(inactive.Id));

        var byUsername = await db.SendAsync(new UserSearchQuery("SEARCH."));
        var byLastName = await db.SendAsync(new UserSearchQuery("поисков"));

        Assert.Equal("search.active", Assert.Single(byUsername).Username);
        Assert.Equal("search.active", Assert.Single(byLastName).Username);
    }

    [Fact]
    public async Task Search_Should_EscapeLikeWildcards()
    {
        await CreateAsync("wild.card");

        var result = await db.SendAsync(new UserSearchQuery("%"));

        Assert.Empty(result);
    }
}
