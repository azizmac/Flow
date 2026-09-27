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

internal sealed class ProjectAccess(IBoardMemberRepository members) : IProjectAccess
{
    public async Task<ProjectAccessInfo> GetAsync(User actor, Guid boardId, CancellationToken cancellationToken)
    {
        // Проекта нет — считаем открытым без ограничений: хендлер ответит 404 по своему пути (загрузка сущности),
        // а глобальному Admin+ проверка прав не должна мешать увидеть этот 404.
        var data = await members.GetAccessDataAsync(boardId, actor.Id, cancellationToken);
        var role = ProjectRoles.Effective(actor.Role, data?.Visibility ?? BoardVisibility.Open, data?.DefaultRole, data?.MemberRole);
        return ProjectRoles.Access(boardId, role);
    }

    public async Task<IReadOnlyCollection<Guid>?> VisibleBoardIdsAsync(User actor, CancellationToken cancellationToken) =>
        actor.Role >= UserRole.Admin ? null : await members.GetVisibleBoardIdsAsync(actor.Id, cancellationToken);
}
