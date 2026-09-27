using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Security;

/// <summary>
/// Роль actor'а в проекте — один лёгкий запрос (потолок проекта + участие) на каждую проверку. Не кэширует
/// намеренно: снятие участника должно действовать сразу, а время жизни scope не всегда равно запросу
/// (корневой провайдер в тестах, IMediator, взятый компонентом Blazor на весь circuit).
/// </summary>
public interface IProjectAccess
{
    Task<ProjectAccessInfo> GetAsync(User actor, Guid boardId, CancellationToken cancellationToken);
}

internal sealed class ProjectAccess(IBoardMemberRepository members) : IProjectAccess
{
    public async Task<ProjectAccessInfo> GetAsync(User actor, Guid boardId, CancellationToken cancellationToken)
    {
        // Проекта нет — роль как у проекта без ограничений: хендлер всё равно ответит 404 раньше или позже.
        var data = await members.GetAccessDataAsync(boardId, actor.Id, cancellationToken);
        var role = ProjectRoles.Effective(actor.Role, data?.DefaultRole, data?.MemberRole);
        return ProjectRoles.Access(boardId, role);
    }
}
