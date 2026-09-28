using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Features.Boards.Commands.BoardVisibilitySetCommand;
using Flow.Application.Features.Search.Queries.SearchQuery;
using Flow.Application.Features.Search.Queries.SimilarTasksQuery;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Search;
using Xunit;
using SharedUserRole = Flow.Domain.Entities.UserRole;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Приватные проекты в SQL (docs/TZ_project_access.md, этап 4B): фильтр видимых проектов стоит в общем условии
/// обеих половин поиска и внутри HNSW-запроса похожих задач — иначе окно ближайших заполнили бы невидимые,
/// и выдача не участника опустела бы. Список задач и счётчики тоже считаются только по видимым.
/// </summary>
[Collection(SearchCollection.Name)]
public class PrivateProjectSearchTests(SearchFixture fixture)
{
    private static readonly Guid Owner = SearchFixture.OwnerId;

    private async Task<Guid> CreateUserAsync(string username) =>
        (await fixture.SendAsync(new UserCreateCommand(Owner, username, $"{username}@example.com", "Имя", "Фамилия",
            "password123", SharedUserRole.Developer))).Response!.Id;

    [Fact]
    public async Task Non_Member_Finds_Only_Open_Projects_In_Search_Similar_And_Lists()
    {
        var hidden = (await fixture.SendAsync(new BoardCreateCommand(Owner, "Скрытый ПРВ", "PRVH"))).Response!;
        var open = (await fixture.SendAsync(new BoardCreateCommand(Owner, "Открытый ПРВ", "PRVO"))).Response!;
        var secret = await fixture.SendAsync(new TaskCreateCommand(Owner, hidden.Id, "Зеленоглазик пурпурный лемур", null, null));
        var visible = await fixture.SendAsync(new TaskCreateCommand(Owner, open.Id, "Зеленоглазик пурпурный лемур тоже", null, null));
        var source = await fixture.SendAsync(new TaskCreateCommand(Owner, open.Id, "Зеленоглазик пурпурный лемур источник", null, null));
        await fixture.SendAsync(new BoardVisibilitySetCommand(Owner, hidden.Id, BoardVisibility.Private));
        await fixture.DrainIndexingAsync();

        var outsider = await CreateUserAsync("prv.outsider");
        var insider = await CreateUserAsync("prv.insider");
        await fixture.SendAsync(new BoardMemberSetCommand(Owner, hidden.Id, insider, ProjectRole.Member));

        foreach (var mode in new[] { SearchMode.Text, SearchMode.Hybrid })
        {
            var outside = await fixture.SendAsync(new SearchQuery(outsider, "зеленоглазик", null, null, false, mode, 50, 0));
            Assert.DoesNotContain(outside!.Items, i => i.SourceId == secret!.Id || i.SourceId == hidden.Id);
            Assert.Contains(outside.Items, i => i.SourceId == visible!.Id);

            var inside = await fixture.SendAsync(new SearchQuery(insider, "зеленоглазик", null, null, false, mode, 50, 0));
            Assert.Contains(inside!.Items, i => i.SourceId == secret!.Id);
        }

        // Прямое попадание по коду скрытой задачи — не попадание.
        var byCode = await fixture.SendAsync(new SearchQuery(outsider, secret!.Code, null, null, false, SearchMode.Hybrid, 20, 0));
        Assert.Null(byCode!.Intent.TaskId);

        var similar = await fixture.SendAsync(new SimilarTasksQuery(outsider, source!.Id, 10));
        Assert.DoesNotContain(similar!, i => i.SourceId == secret.Id);
        Assert.Contains(similar!, i => i.SourceId == visible!.Id);
        Assert.Contains(await fixture.SendAsync(new SimilarTasksQuery(insider, source.Id, 10)) ?? [], i => i.SourceId == secret.Id);

        var list = await fixture.SendAsync(new TaskSearchQuery(outsider, Query: "зеленоглазик", Limit: 500));
        Assert.DoesNotContain(list.Items, t => t.Id == secret.Id);
        Assert.Equal(list.Items.Count, list.Matched);
    }
}
