using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Domain.Templates;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Templates;

// «Применить конфигурацию проекта к проектам» (docs/TZ_workflow_config.md §4, этап 3F). Источник снимается чертежом
// (ToBlueprint), к каждой цели он применяется своей транзакцией: отказ одной цели откатывает только её правки
// (IUnitOfWork.DiscardChanges), итог приходит списком. Права — видеть источник и настраивать каждую цель; проверяются
// все цели до первой правки, чтобы 403 не оставлял половину проектов изменёнными.

public sealed record BoardApplyConfigPreviewQuery(Guid ActorId, Guid SourceBoardId, IReadOnlyList<ApplyConfigTarget> Targets, ConfigParts Parts)
    : IRequest<ApplyConfigPreviewResponse?>;

public sealed record BoardApplyConfigCommand(Guid ActorId, Guid SourceBoardId, IReadOnlyList<ApplyConfigTarget> Targets, ConfigParts Parts)
    : IRequest<ApplyBoardConfigResponse?>;

internal sealed class BoardApplyConfigHandlers(
    IBoardRepository boards,
    ITaskItemRepository tasks,
    BoardConfigApplier applier,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) :
    IRequestHandler<BoardApplyConfigPreviewQuery, ApplyConfigPreviewResponse?>,
    IRequestHandler<BoardApplyConfigCommand, ApplyBoardConfigResponse?>
{
    public const int MaxTargets = 50;

    public async Task<ApplyConfigPreviewResponse?> Handle(BoardApplyConfigPreviewQuery request, CancellationToken cancellationToken)
    {
        var (source, blueprint) = await PrepareAsync(request.ActorId, request.SourceBoardId, request.Targets, request.Parts, cancellationToken);
        if (source is null)
            return null;

        var parts = BoardConfigApplier.ToDomain(request.Parts);
        var previews = new List<ConfigTargetPreview>();
        foreach (var target in request.Targets.DistinctBy(t => t.BoardId))
        {
            var board = await boards.GetByIdAsync(target.BoardId, cancellationToken);
            if (board is null)
                continue;
            var counts = (await tasks.GroupCountAsync(new TaskListFilter(BoardId: board.Id), TaskGroupField.Status, null, cancellationToken))
                .Where(g => g.Key is not null).ToDictionary(g => g.Key!, g => g.Count);
            previews.Add(Preview(board, source, blueprint!, parts, target.StatusMap, counts));
        }

        return new ApplyConfigPreviewResponse(source.Id,
            source.Statuses.OrderBy(s => s.SortOrder).Select(s => new ConfigSourceStatus(s.Id, s.Name, s.IsInitial, s.IsFinal)).ToList(),
            previews);
    }

    public async Task<ApplyBoardConfigResponse?> Handle(BoardApplyConfigCommand request, CancellationToken cancellationToken)
    {
        var (source, blueprint) = await PrepareAsync(request.ActorId, request.SourceBoardId, request.Targets, request.Parts, cancellationToken);
        if (source is null)
            return null;

        var parts = BoardConfigApplier.ToDomain(request.Parts);
        var sourceId = source.Id;
        var results = new List<ApplyConfigTargetResult>();
        foreach (var target in request.Targets.DistinctBy(t => t.BoardId))
        {
            if (target.BoardId == sourceId)
            {
                results.Add(new ApplyConfigTargetResult(target.BoardId, false, false, 0, "Это проект-источник."));
                continue;
            }

            try
            {
                var board = await boards.GetByIdAsync(target.BoardId, cancellationToken)
                            ?? throw new InvalidOperationException("Проект не найден.");
                var before = BoardConfigApplier.Fingerprint(board);
                var moved = await applier.ApplyAsync(board, blueprint!, parts, ToKeys(target.StatusMap), request.ActorId, cancellationToken);
                if (moved == 0 && BoardConfigApplier.Fingerprint(board) == before)
                {
                    unitOfWork.DiscardChanges();
                    results.Add(new ApplyConfigTargetResult(board.Id, true, false, 0, null));
                    continue;
                }

                // Имена статусов пишутся в два шага одной транзакцией: обмен имён (A↔B) иначе упёрся бы в unique (BoardId, Name).
                await unitOfWork.InTransactionAsync(async () =>
                {
                    var names = board.ParkStatusNames();
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    board.RestoreStatusNames(names);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }, cancellationToken);
                results.Add(new ApplyConfigTargetResult(board.Id, true, true, moved, null));
            }
            // Любой отказ записи (не только доменный: конфликт в БД тоже) — итог этого проекта, а не всего запроса.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                unitOfWork.DiscardChanges();
                results.Add(new ApplyConfigTargetResult(target.BoardId, false, false, 0, ex.Message));
            }
        }

        return new ApplyBoardConfigResponse(results);
    }

    /// <summary>Права на источник и все цели — до первой правки; чертёж источника. (null, null) — источника нет.</summary>
    private async Task<(Board? Source, BoardBlueprint? Blueprint)> PrepareAsync(Guid actorId, Guid sourceId, IReadOnlyList<ApplyConfigTarget> targets,
        ConfigParts parts, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(actorId, cancellationToken);
        if (targets.Count == 0)
            throw new ArgumentException("Выберите проекты, к которым применить настройки.", nameof(targets));
        if (targets.Count > MaxTargets)
            throw new ArgumentException($"Не больше {MaxTargets} проектов за раз.", nameof(targets));
        if (BoardConfigApplier.ToDomain(parts) == BlueprintParts.None)
            throw new ArgumentException("Выберите, что переносить.", nameof(parts));

        if (!(await projectAccess.GetAsync(actor, sourceId, cancellationToken)).CanView)
            return (null, null);
        foreach (var target in targets.Select(t => t.BoardId).Distinct())
            permissions.EnsureCanManageConfig(await projectAccess.GetAsync(actor, target, cancellationToken));

        var source = await boards.GetByIdAsync(sourceId, cancellationToken);
        return source is null ? (null, null) : (source, source.ToBlueprint());
    }

    private static Dictionary<Guid, string>? ToKeys(IReadOnlyDictionary<Guid, Guid>? map) =>
        map?.ToDictionary(p => p.Key, p => p.Value.ToString("N"));

    /// <summary>Что случится с целью — без правок: считается по той же карте, что и применение.</summary>
    private static ConfigTargetPreview Preview(Board board, Board source, BoardBlueprint blueprint, BlueprintParts parts,
        IReadOnlyDictionary<Guid, Guid>? requested, IReadOnlyDictionary<string, int> counts)
    {
        string? error = null;
        var map = new Dictionary<Guid, string>(board.SuggestStatusMap(blueprint));
        foreach (var (statusId, sourceStatusId) in requested ?? new Dictionary<Guid, Guid>())
            if (map.ContainsKey(statusId) && source.Statuses.Any(s => s.Id == sourceStatusId))
                map[statusId] = sourceStatusId.ToString("N");

        var ordered = board.Statuses.OrderBy(s => s.SortOrder).ToList();
        var kept = new Dictionary<string, Status>();
        foreach (var status in ordered)
            kept.TryAdd(map[status.Id], status);
        int Count(Status s) => counts.GetValueOrDefault(s.Id.ToString(), 0);
        string SourceName(string key) => blueprint.Statuses.First(b => b.Key == key).Name;

        var statusMap = ordered.Select(s => new ConfigStatusMapping(s.Id, s.Name, Count(s), Guid.ParseExact(map[s.Id], "N"), kept[map[s.Id]] == s)).ToList();
        List<string> created = [], renamed = [], removed = [];
        var moved = 0;
        if (parts.HasFlag(BlueprintParts.Statuses))
        {
            created = blueprint.Statuses.Where(b => !kept.ContainsKey(b.Key)).Select(b => b.Name).ToList();
            renamed = kept.Where(p => p.Value.Name != SourceName(p.Key)).Select(p => $"{p.Value.Name} → {SourceName(p.Key)}").ToList();
            foreach (var status in ordered.Where(s => kept[map[s.Id]] != s))
            {
                removed.Add($"{status.Name} ({Count(status)} → {SourceName(map[status.Id])})");
                moved += Count(status);
            }
        }

        var typesAdded = parts.HasFlag(BlueprintParts.Types)
            ? blueprint.TaskTypes.Where(b => board.TaskTypes.All(t => !string.Equals(t.Name, b.Name, StringComparison.OrdinalIgnoreCase))).Select(b => b.Name).ToList()
            : [];
        List<string> fieldsAdded = [], fieldsUpdated = [];
        if (parts.HasFlag(BlueprintParts.Fields))
            foreach (var b in blueprint.CustomFields)
            {
                var field = board.CustomFields.FirstOrDefault(f => f.Key == b.Key);
                if (field is null)
                    fieldsAdded.Add(b.Name);
                else if (field.Type != b.Type)
                    error ??= $"Поле «{field.Name}» ({field.Key}) в проекте другого вида — значения задач не перенести.";
                else if (field.Name != b.Name || field.IsRequired != b.IsRequired || field.IsArchived
                         || (field.HasOptions && !field.Options.Select(o => o.Label).SequenceEqual(b.Options ?? [])))
                    fieldsUpdated.Add(b.Name);
            }

        if (board.Id == source.Id)
            error = "Это проект-источник.";
        return new ConfigTargetPreview(board.Id, board.Key, board.Name, statusMap, created, renamed, removed, moved, typesAdded, fieldsAdded, fieldsUpdated, error);
    }
}
