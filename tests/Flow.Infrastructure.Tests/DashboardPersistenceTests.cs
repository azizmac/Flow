using System.Text.Json;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.CustomFields;
using Flow.Application.Features.Dashboards;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Dashboards;
using Microsoft.EntityFrameworkCore;
using Xunit;
using DomainWidgetType = Flow.Domain.Entities.WidgetType;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Дашборды на реальном Postgres (этап 2G): виджеты с настройками в jsonb переживают перезагрузку и уходят вместе
/// с дашбордом; разбивка — GROUP BY под FQL-условием (фейк его не применяет), по варианту пользовательского поля
/// (MultiSelect считается по каждому варианту) и «создано / закрыто» по дням.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DashboardPersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    private async Task<(Guid Dashboard, Guid Widget)> WidgetAsync(DomainWidgetType type, WidgetConfig config)
    {
        var dashboard = await db.SendAsync(new DashboardCreateCommand(Owner, "Обзор", false));
        var updated = (await db.SendAsync(new WidgetAddCommand(Owner, dashboard.Id, type, "Виджет", config)))!;
        return (dashboard.Id, updated.Widgets.Single().Id);
    }

    private async Task<WidgetDataResponse> DataAsync((Guid Dashboard, Guid Widget) widget) =>
        (await db.SendAsync(new WidgetDataQuery(Owner, widget.Dashboard, widget.Widget)))!;

    [Fact]
    public async Task Widgets_Round_Trip_And_Go_Away_With_The_Dashboard()
    {
        var widget = await WidgetAsync(DomainWidgetType.TaskList, new WidgetConfig(Fql: "priority >= high", Limit: 5));

        var reloaded = (await db.SendAsync(new DashboardGetQuery(Owner, widget.Dashboard)))!;
        Assert.Equal(new WidgetConfig(Fql: "priority >= high", Limit: 5), reloaded.Widgets.Single().Config);
        Assert.Equal((0, 0, 6, 3), (reloaded.Widgets[0].X, reloaded.Widgets[0].Y, reloaded.Widgets[0].W, reloaded.Widgets[0].H));

        Assert.True(await db.SendAsync(new DashboardDeleteCommand(Owner, widget.Dashboard)));
        Assert.False(await db.QueryAsync(ctx => ctx.Set<DashboardWidget>().AnyAsync(w => w.Id == widget.Widget)));
    }

    [Fact]
    public async Task Breakdown_Groups_In_Sql_Under_The_Fql_Condition()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Разбивка", "DSHB"))).Response!;
        var withTags = (await db.SendAsync(new CustomFieldCreateCommand(Owner, board.Id, "tags", "Метки", CustomFieldType.MultiSelect, ["UI", "API"])))!;
        var tags = withTags.CustomFields!.Single();
        var ui = tags.Options[0].Id;
        var api = tags.Options[1].Id;

        async Task<Guid> Task(string title, string? tagsJson = null) =>
            (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, title, null, null,
                CustomFields: tagsJson is null ? null : new Dictionary<Guid, JsonElement?> { [tags.Id] = JsonDocument.Parse(tagsJson).RootElement.Clone() })))!.Id;

        await Task("Обе метки", $"[\"{ui}\", \"{api}\"]");
        await Task("Только UI", $"[\"{ui}\"]");
        var done = await Task("Без меток");
        await db.SendAsync(new TaskUpdateCommand(Owner, done, null, null, board.Statuses.Single(s => s.IsFinal).Id));

        var byStatus = await DataAsync(await WidgetAsync(DomainWidgetType.Breakdown, new WidgetConfig(Fql: "project = DSHB", GroupBy: "status")));
        Assert.Equal([(board.Statuses.Single(s => s.IsInitial).Name, 2), (board.Statuses.Single(s => s.IsFinal).Name, 1)],
            byStatus.Groups!.Select(g => (g.Label, g.Count)));

        var byTag = await DataAsync(await WidgetAsync(DomainWidgetType.Breakdown, new WidgetConfig(Fql: "project = DSHB", GroupBy: "cf:tags")));
        Assert.Equal([("UI", 2), ("API", 1), ("Не заполнено", 1)], byTag.Groups!.Select(g => (g.Label, g.Count)));

        var daily = await DataAsync(await WidgetAsync(DomainWidgetType.CreatedVsClosed, new WidgetConfig(Fql: "project = DSHB", Days: 7)));
        Assert.Equal((3, 1), (daily.Days![^1].Created, daily.Days[^1].Closed));

        var counter = await DataAsync(await WidgetAsync(DomainWidgetType.Counter, new WidgetConfig(Fql: "project = DSHB AND status != Сделана")));
        Assert.Equal(2, counter.Count);
    }
}
