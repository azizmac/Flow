using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Domain.Templates;
using Flow.Shared.Contracts.Search;

namespace Flow.Application.Features.Templates;

/// <summary>
/// Применение чертежа к живому проекту (этап 3F) — общий путь для «применить конфигурацию к проектам». Задачи
/// лишних статусов переезжают служебно (как при удалении статуса: без проверки workflow, с журналом и
/// <c>Upsert</c> в поиске), задачи статусов, у которых поменялась финальность, переиндексируются (меняется IsClosed).
/// Типы и поля только добавляются и правятся: на лишних лежат задачи и их значения.
/// </summary>
internal sealed class BoardConfigApplier(ITaskItemRepository tasks, ITaskActivityRepository activities, ISearchIndexQueue searchIndex)
{
    public static BlueprintParts ToDomain(Flow.Shared.Contracts.Boards.ConfigParts parts) => (BlueprintParts)(int)parts & BlueprintParts.All;

    /// <summary>Сколько задач переехало; доска изменена в памяти, сохраняет вызывающий.</summary>
    public async Task<int> ApplyAsync(Board board, BoardBlueprint blueprint, BlueprintParts parts, IReadOnlyDictionary<Guid, string>? statusMap,
        Guid actorId, CancellationToken cancellationToken)
    {
        var moved = 0;
        IReadOnlyDictionary<string, Guid> statusIds;
        if (parts.HasFlag(BlueprintParts.Statuses))
        {
            var wasFinal = board.Statuses.ToDictionary(s => s.Id, s => s.IsFinal);
            var result = board.ApplyBlueprintStatuses(blueprint, statusMap);
            foreach (var (statusId, moveTo) in result.Removals)
            {
                board.RemoveStatus(statusId, moveTo);
                foreach (var task in await tasks.GetByStatusIdAsync(statusId, cancellationToken))
                {
                    activities.Add(TaskActivity.StatusChanged(task.Id, actorId, task.StatusId, moveTo));
                    task.ChangeStatus(moveTo);
                    searchIndex.Enqueue(SearchSourceType.Task, task.Id, board.Id, SearchIndexOperation.Upsert);
                    moved++;
                }
            }

            foreach (var status in board.Statuses.Where(s => wasFinal.TryGetValue(s.Id, out var was) && was != s.IsFinal))
                foreach (var task in await tasks.GetByStatusIdAsync(status.Id, cancellationToken))
                    searchIndex.Enqueue(SearchSourceType.Task, task.Id, board.Id, SearchIndexOperation.Upsert);

            statusIds = result.StatusIds;
        }
        else
        {
            statusIds = board.MapBlueprintStatuses(blueprint, statusMap);
        }

        var typeIds = parts.HasFlag(BlueprintParts.Types) ? board.ApplyBlueprintTypes(blueprint, archiveExtras: false) : board.MapBlueprintTypes(blueprint);
        var fieldIds = parts.HasFlag(BlueprintParts.Fields) ? board.ApplyBlueprintFields(blueprint, typeIds) : board.MapBlueprintFields(blueprint);
        if (parts.HasFlag(BlueprintParts.Workflow))
            board.ApplyBlueprintWorkflow(blueprint, statusIds, typeIds, fieldIds);
        if (parts.HasFlag(BlueprintParts.Screens))
            board.ApplyBlueprintScreens(blueprint, typeIds, fieldIds);
        return moved;
    }

    /// <summary>Снимок для сравнения «до/после»: одинаковый JSON — повторное применение ничего не изменило.</summary>
    public static string Fingerprint(Board board) => BoardBlueprintJson.Serialize(board.ToBlueprint());
}
