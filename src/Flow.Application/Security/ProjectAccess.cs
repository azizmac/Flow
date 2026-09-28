using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Security;

/// <summary>
/// Роль actor'а в проекте и видимость проектов (docs/TZ_project_access.md, этапы 4A–4B). Один лёгкий запрос
/// на проверку. Не кэширует намеренно: снятие участника должно действовать сразу, а время жизни scope не всегда
/// равно запросу (корневой провайдер в тестах, IMediator, взятый компонентом Blazor на весь circuit).
/// </summary>
public interface IProjectAccess
{
    /// <summary>Роль и права; Role = null — проект не виден (или его нет).</summary>
    Task<ProjectAccessInfo> GetAsync(User actor, Guid boardId, CancellationToken cancellationToken);

    /// <summary>
    /// Видимые проекты для фильтра в запросах чтения; null — actor видит все (глобальный Admin+ или
    /// приватных проектов нет) и фильтр не нужен.
    /// </summary>
    Task<IReadOnlyCollection<Guid>?> VisibleBoardIdsAsync(User actor, CancellationToken cancellationToken);
}

internal sealed class ProjectAccess(IBoardMemberRepository members, IPermissionSetRepository permissionSets) : IProjectAccess
{
    public async Task<ProjectAccessInfo> GetAsync(User actor, Guid boardId, CancellationToken cancellationToken)
    {
        // Проекта нет — считаем открытым без ограничений: хендлер ответит 404 по своему пути (загрузка сущности),
        // а глобальному Admin+ проверка прав не должна мешать увидеть этот 404.
        var data = await members.GetAccessDataAsync(boardId, actor.Id, cancellationToken)
                   ?? new BoardAccessData(boardId, BoardVisibility.Open, null, []);
        return ProjectRoles.Resolve(actor.Role, data, await SetsAsync(permissionSets, [data], cancellationToken));
    }

    /// <summary>Свои наборы участий — отдельным запросом и только когда они вообще есть (обычно нет).</summary>
    public static async Task<IReadOnlyDictionary<Guid, PermissionSet>> SetsAsync(
        IPermissionSetRepository repository, IEnumerable<BoardAccessData> data, CancellationToken cancellationToken)
    {
        var ids = data.SelectMany(d => d.Grants).Select(g => g.PermissionSetId).OfType<Guid>().Distinct().ToList();
        return ids.Count == 0
            ? new Dictionary<Guid, PermissionSet>()
            : (await repository.GetByIdsAsync(ids, cancellationToken)).ToDictionary(s => s.Id);
    }

    public async Task<IReadOnlyCollection<Guid>?> VisibleBoardIdsAsync(User actor, CancellationToken cancellationToken) =>
        actor.Role >= UserRole.Admin ? null : await members.GetVisibleBoardIdsAsync(actor.Id, cancellationToken);
}
