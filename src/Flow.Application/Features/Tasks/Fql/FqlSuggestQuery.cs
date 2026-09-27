using Flow.Domain.Entities;
using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Filters;
using MediatR;

namespace Flow.Application.Features.Tasks.Fql;

/// <summary>Подсказки FQL у курсора (docs/TZ_task_views.md §7): поля, операторы, значения поля, связки.</summary>
public sealed record FqlSuggestQuery(Guid ActorId, string Query, int Position) : IRequest<FqlSuggestResponse>;

internal sealed class FqlSuggestQueryHandler(
    IBoardRepository boards,
    IUserRepository users,
    ITaskItemRepository tasks,
    ITaskLinkRepository links,
    ISprintRepository sprints,
    IMilestoneRepository milestones,
    ActorResolver actors,
    IProjectAccess projectAccess) : IRequestHandler<FqlSuggestQuery, FqlSuggestResponse>
{
    private const int Limit = 12;

    public async Task<FqlSuggestResponse> Handle(FqlSuggestQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var cursor = FqlSuggester.Analyze(request.Query ?? "", request.Position);
        var lookup = new FqlLookup(actor, boards, users, tasks, links, projectAccess, sprints, milestones);

        var visible = await lookup.VisibleBoardsAsync(cancellationToken);
        var customKeys = visible.SelectMany(b => b.CustomFields).Where(f => !f.IsArchived)
            .GroupBy(f => f.Key).Select(g => new FqlSuggestion($"cf.{g.Key} ", $"cf.{g.Key}", g.First().Name, "field"));

        var items = cursor.Slot switch
        {
            FqlSlot.Field => FqlFields.All.Select(f => new FqlSuggestion(f.Name + " ", f.Name, f.Hint, "field"))
                .Concat(customKeys)
                .Append(new FqlSuggestion("NOT ", "NOT", "отрицание условия", "keyword"))
                .Append(new FqlSuggestion("ORDER BY ", "ORDER BY", "порядок", "keyword")),
            FqlSlot.Operator => FqlSuggester.OperatorsFor(cursor.Field).Select(op => new FqlSuggestion(op + " ", op, null, "operator")),
            FqlSlot.Value => await ValuesAsync(cursor, lookup, cancellationToken),
            FqlSlot.Connector => [new("AND ", "AND", null, "keyword"), new("OR ", "OR", null, "keyword"), new("ORDER BY ", "ORDER BY", null, "keyword")],
            FqlSlot.OrderField => FqlFields.Orders.Keys.Select(k => new FqlSuggestion(k, k, null, "field")),
            _ => [new(" ASC", "ASC", "по возрастанию", "keyword"), new(" DESC", "DESC", "по убыванию", "keyword"), new(", ", ",", "ещё поле", "keyword")]
        };

        var partial = cursor.Partial.Trim('"', '\'', '@');
        var filtered = items
            .Where(i => partial.Length == 0 || i.Label.TrimStart('@').StartsWith(partial, StringComparison.OrdinalIgnoreCase)
                        || i.Label.Contains(partial, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Label.TrimStart('@').StartsWith(partial, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Take(Limit)
            .ToList();

        return new FqlSuggestResponse(filtered, cursor.ReplaceFrom, cursor.ReplaceLength);
    }

    private async Task<IEnumerable<FqlSuggestion>> ValuesAsync(FqlCursor cursor, IFqlLookup lookup, CancellationToken ct)
    {
        FqlSuggestion Value(string value, string? hint = null) => new(FqlSuggester.Quote(value), value, hint, "value");
        FqlSuggestion Function(string fn, string? hint = null) => new(fn, fn, hint, "value");

        var visible = await lookup.VisibleBoardsAsync(ct);
        if (cursor.Field is { } cf && cf.StartsWith("cf.", StringComparison.OrdinalIgnoreCase))
        {
            var fields = visible.SelectMany(b => b.CustomFields).Where(f => string.Equals(f.Key, cf[3..], StringComparison.OrdinalIgnoreCase)).ToList();
            return fields.FirstOrDefault()?.Type switch
            {
                CustomFieldType.Select or CustomFieldType.MultiSelect =>
                    fields.SelectMany(f => f.Options).Select(o => o.Label).Distinct(StringComparer.OrdinalIgnoreCase).Select(l => Value(l)).Append(Function("EMPTY", "не заполнено")),
                CustomFieldType.Checkbox => [Function("true", "отмечено"), Function("false", "не отмечено")],
                CustomFieldType.User => [Function("me()", "я"), Function("EMPTY", "не заполнено")],
                CustomFieldType.Date => FqlFields.DateFunctions.Select(f => Function(f)).Append(Function(DateTime.UtcNow.ToString("yyyy-MM-dd"), "дата")),
                _ => [Function("EMPTY", "не заполнено")]
            };
        }

        switch (cursor.Field?.ToLowerInvariant())
        {
            case "project":
                return visible.Select(b => Value(b.Key, b.Name));
            case "status":
                return visible.SelectMany(b => b.Statuses).Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Select(n => Value(n));
            case "type":
                return visible.SelectMany(b => b.TaskTypes).Where(t => !t.IsArchived).Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Select(n => Value(n));
            case "typekind":
                return FqlFields.Kinds.Keys.Where(k => k.All(char.IsAscii)).Select(k => Value(k));
            case "statuscategory":
                return FqlFields.StatusTypes.Keys.Where(k => k.All(char.IsAscii)).Select(k => Value(k));
            case "priority":
                return FqlFields.Priorities.Keys.Where(k => k.All(char.IsAscii)).Select(k => Value(k));
            case "assignee" or "creator":
                var found = await users.SearchAsync(cursor.Partial.Trim('"', '\'', '@'), Limit, ct);
                return new[] { Function("me()", "я") }
                    .Concat(cursor.Field.Equals("assignee", StringComparison.OrdinalIgnoreCase) ? [Function("EMPTY", "не назначена")] : [])
                    .Concat(found.Select(u => new FqlSuggestion("@" + u.Username, "@" + u.Username, $"{u.FirstName} {u.LastName}".Trim(), "value")));
            case "parent":
                return [Function("childrenOf()", "всё поддерево задачи"), Function("EMPTY", "верхний уровень")];
            case "linked":
                return FqlFields.LinkFunctions.Select(f => Function(f)).Append(Function("EMPTY", "без связей"));
            case "created" or "updated" or "start" or "due":
                return FqlFields.DateFunctions.Select(f => Function(f))
                    .Concat([Function("-7d", "неделю назад"), Function("7d", "через неделю"), Function(DateTime.UtcNow.ToString("yyyy-MM-dd"), "дата")]);
            case "estimate":
                return [Function("30m"), Function("2h"), Function("1d")];
            case "sprint":
                var sprints = await lookup.SprintsAsync(ct);
                return new[] { Function("openSprints()", "незавершённые"), Function("closedSprints()", "завершённые"), Function("EMPTY", "бэклог") }
                    .Concat(sprints.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Select(n => Value(n)));
            case "milestone":
                var milestones = await lookup.MilestonesAsync(ct);
                return new[] { Function("openMilestones()", "открытые"), Function("closedMilestones()", "закрытые"), Function("EMPTY", "без вехи") }
                    .Concat(milestones.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Select(n => Value(n)));
            default:
                return [];
        }
    }
}
