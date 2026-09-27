using System.Text.Json;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.CustomFields;
using Flow.Application.Features.Search.Queries.SearchQuery;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Пользовательские поля на реальном Postgres (этап 1D): варианты в jsonb и типы задач в uuid[] переживают
/// перезагрузку, значения — jsonb у задачи, FQL `cf.*` через SQL-функции flow_cf_* для каждого вида поля.
/// </summary>
[Collection(PostgresCollection.Name)]
public class CustomFieldPersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    private static JsonElement? Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task Definitions_And_Values_Round_Trip_And_Fql_Translates()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Поля", "CFP"))).Response!;
        var bug = board.TaskTypes.Single(t => t.Kind == Flow.Shared.Contracts.Boards.TaskTypeKind.Bug).Id;
        async Task<BoardResponse> Add(string key, CustomFieldType type, IReadOnlyList<string>? options = null, IReadOnlyList<Guid>? types = null) =>
            (await db.SendAsync(new CustomFieldCreateCommand(Owner, board.Id, key, key, type, options, TaskTypeIds: types)))!;

        await Add("sla", CustomFieldType.Select, ["Gold", "Silver"]);
        await Add("tags", CustomFieldType.MultiSelect, ["UI", "API"]);
        await Add("budget", CustomFieldType.Number);
        await Add("deadline", CustomFieldType.Date);
        await Add("urgent", CustomFieldType.Checkbox);
        var full = await Add("note", CustomFieldType.Text, types: [bug, board.TaskTypes.Single(t => t.IsDefault).Id]);
        var f = full.CustomFields!.ToDictionary(x => x.Key);

        // Перечитанный проект: варианты (jsonb) и типы задач (uuid[]) на месте.
        var reloaded = await db.QueryAsync(ctx => ctx.CustomFields.AsNoTracking().Where(x => x.BoardId == board.Id).ToListAsync());
        Assert.Equal(["Gold", "Silver"], reloaded.Single(x => x.Key == "sla").Options.Select(o => o.Label));
        Assert.Equal(2, reloaded.Single(x => x.Key == "note").TaskTypeIds.Count);

        async Task<Guid> TaskWith(string title, Dictionary<Guid, JsonElement?> values) =>
            (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, title, null, null, CustomFields: values)))!.Id;

        var gold = await TaskWith("Золото", new()
        {
            [f["sla"].Id] = Json($"\"{f["sla"].Options[0].Id}\""),
            [f["tags"].Id] = Json($"[\"{f["tags"].Options[0].Id}\", \"{f["tags"].Options[1].Id}\"]"),
            [f["budget"].Id] = Json("150.5"),
            [f["deadline"].Id] = Json("\"2026-12-01\""),
            [f["urgent"].Id] = Json("true"),
            [f["note"].Id] = Json("\"Клиент 100% важный\"")
        });
        var empty = await TaskWith("Пусто", []);

        async Task<IEnumerable<Guid>> Fql(string q) =>
            (await db.SendAsync(new TaskSearchQuery(Owner, board.Id, Fql: q))).Items.Select(t => t.Id);

        Assert.Equal([gold], await Fql("cf.sla = gold"));
        Assert.Equal([gold], await Fql("cf.tags in (API)"));
        Assert.Equal([gold], await Fql("cf.budget > 100"));
        Assert.Equal([gold], await Fql("cf.deadline <= 2026-12-01"));
        Assert.Equal([gold], await Fql("cf.urgent = true"));
        Assert.Equal([empty], await Fql("cf.urgent = false"));
        // % и _ в строке — буквы, а не шаблон: ILIKE с явным ESCAPE (без него Npgsql ставит ESCAPE '').
        Assert.Equal([gold], await Fql("cf.note ~ \"100%\""));
        Assert.Empty(await Fql("cf.note ~ \"1_0\""));
        Assert.Equal(new[] { gold, empty }.Order().ToList(), (await Fql("cf.note ~ \"клиент\" or cf.sla is empty")).Order().ToList());
        Assert.Equal([empty], await Fql("cf.budget is empty"));
        Assert.Equal([empty], await Fql("cf.sla != gold"));
    }
}

/// <summary>Текстовые поля — часть содержания задачи в поиске (docs/TZ_task_model.md §4).</summary>
[Collection(SearchCollection.Name)]
public class CustomFieldSearchTests(SearchFixture fixture)
{
    [Fact]
    public async Task Text_Field_Value_Is_Found_By_Search()
    {
        var owner = SearchFixture.OwnerId;
        var board = (await fixture.SendAsync(new BoardCreateCommand(owner, "Поиск полей", "CFS"))).Response!;
        var withField = (await fixture.SendAsync(new CustomFieldCreateCommand(owner, board.Id, "steps", "Шаги", CustomFieldType.LongText)))!;
        var task = (await fixture.SendAsync(new TaskCreateCommand(owner, board.Id, "Падает экран", null, null)))!;
        await fixture.SendAsync(new TaskSetCustomFieldsCommand(owner, task.Id, new Dictionary<Guid, JsonElement?>
        {
            [withField.CustomFields![0].Id] = JsonDocument.Parse("\"Нажать фиолетовую каракатицу\"").RootElement.Clone()
        }));
        await fixture.DrainIndexingAsync();

        var result = await fixture.SendAsync(new SearchQuery(owner, "каракатицу", null, null, false, SearchMode.Text, 20, 0));

        Assert.Contains(result!.Items, i => i.SourceId == task.Id);
    }
}
