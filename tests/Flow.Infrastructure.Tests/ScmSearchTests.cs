using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Search.Commands.ReindexCommand;
using Flow.Application.Features.Search.Queries.SearchQuery;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;
using Flow.Infrastructure.Persistence;
using Flow.Shared.Contracts.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Этап 5E на живом pgvector: PR и коммиты задачи индексируются (заголовок, ветки, короткий sha), ветки — нет;
/// выдача несёт код задачи и саму задачу как родителя; удаление задачи убирает их чанки.
/// </summary>
[Collection(SearchCollection.Name)]
public class ScmSearchTests(SearchFixture fixture)
{
    private static readonly Guid Owner = SearchFixture.OwnerId;

    [Fact]
    public async Task Pull_Requests_And_Commits_Are_Found_Through_Their_Task()
    {
        var board = (await fixture.SendAsync(new BoardCreateCommand(Owner, "Разработка", "SCMS"))).Response!;
        var task = (await fixture.SendAsync(new TaskCreateCommand(Owner, board.Id, "Форма входа", null, null)))!;

        var connection = GitHostConnection.Create(GitProvider.GitHub, "GitHub", null, "p:tok", Owner);
        var repository = GitRepository.Create(connection.Id, "51", "acme/scms", "https://github.com/acme/scms", "main", "p:secret");
        var pr = GitDevelopmentLink.Create(task.Id, repository.Id, GitDevelopmentLinkKind.PullRequest, "42");
        pr.Apply("https://github.com/acme/scms/pull/42", "Капча на форме входа", GitDevelopmentLinkState.Open, "octocat", null, "SCMS-1-kapcha", "main", DateTime.UtcNow);
        var commit = GitDevelopmentLink.Create(task.Id, repository.Id, GitDevelopmentLinkKind.Commit, "a1b2c3d4e5f6");
        commit.Apply("https://github.com/acme/scms/commit/a1b2c3d4e5f6", "SCMS-1 поправил валидацию телефона", null, "octocat", null, "main", null, DateTime.UtcNow);
        var branch = GitDevelopmentLink.Create(task.Id, repository.Id, GitDevelopmentLinkKind.Branch, "SCMS-1-kapcha");
        branch.Apply("https://github.com/acme/scms/tree/SCMS-1-kapcha", "SCMS-1-kapcha", GitDevelopmentLinkState.Open, null, null, "SCMS-1-kapcha", null, DateTime.UtcNow);
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowDbContext>();
            db.AddRange(connection, repository, pr, commit, branch);
            await db.SaveChangesAsync();
        }

        Assert.Equal(2, await fixture.SendAsync(new ReindexCommand(Owner, [SearchSourceType.Development], board.Id)));
        await fixture.DrainIndexingAsync();

        var prChunk = await fixture.QueryAsync(db => db.SearchChunks.SingleAsync(c => c.SourceType == SearchSourceType.Development && c.SourceId == pr.Id));
        Assert.Equal("PR #42 Капча на форме входа\nSCMS-1-kapcha → main", prChunk.Content);
        Assert.Equal(board.Id, prChunk.BoardId);
        var commitChunk = await fixture.QueryAsync(db => db.SearchChunks.SingleAsync(c => c.SourceId == commit.Id));
        Assert.Equal("SCMS-1 поправил валидацию телефона\nкоммит a1b2c3d", commitChunk.Content);
        Assert.False(await fixture.QueryAsync(db => db.SearchChunks.AnyAsync(c => c.SourceId == branch.Id)));

        var found = (await fixture.SendAsync(new SearchQuery(Owner, "валидацию телефона", [SearchSourceType.Development], board.Id, false, SearchMode.Text, 10, 0)))!;
        var hit = Assert.Single(found.Items);
        Assert.Equal((commit.Id, task.Id, "SCMS-1"), (hit.SourceId, hit.ParentId, hit.TaskCode));
        Assert.Equal("SCMS-1 поправил валидацию телефона", hit.Title);

        Assert.True(await fixture.SendAsync(new TaskDeleteCommand(Owner, task.Id)));
        await fixture.DrainIndexingAsync();
        Assert.False(await fixture.QueryAsync(db => db.SearchChunks.AnyAsync(c => c.SourceType == SearchSourceType.Development && (c.SourceId == pr.Id || c.SourceId == commit.Id))));
    }
}
