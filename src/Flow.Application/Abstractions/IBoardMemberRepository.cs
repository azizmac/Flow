using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>
/// Данные для расчёта роли в проекте: потолок проекта и участие человека — прямое и через группы (этап 4C), у каждого
/// участия своя роль и, возможно, свой набор прав (этап 4E). MemberRole — максимум ролей участия.
/// </summary>
public sealed record BoardAccessData(Guid BoardId, BoardVisibility Visibility, ProjectRole? DefaultRole, IReadOnlyList<AccessGrant> Grants)
{
    public ProjectRole? MemberRole => Grants.Count == 0 ? null : Grants.Max(g => g.Role);
}

/// <summary>Одно участие: роль (для набора — его базовая) и набор прав; null — права самой роли.</summary>
public sealed record AccessGrant(ProjectRole Role, Guid? PermissionSetId = null);

public sealed record BoardGroupView(BoardGroup Link, string Name, int MemberCount);

/// <summary>Прямое участие людей в проектах (docs/TZ_project_access.md, этап 4A).</summary>
public interface IBoardMemberRepository
{
    /// <summary>Одним запросом: DefaultRole проекта и роль участия человека; null — проекта нет.</summary>
    Task<BoardAccessData?> GetAccessDataAsync(Guid boardId, Guid userId, CancellationToken cancellationToken);

    /// <summary>То же по всем проектам сразу — для GET /boards/my-access (клиент решает, какие кнопки показывать).</summary>
    Task<IReadOnlyList<BoardAccessData>> GetAccessDataForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Проекты, которые человек видит без глобальной роли Admin+: открытые и те, где он участник.
    /// null — приватных проектов нет вовсе, фильтровать нечего (установка без приватности не платит за фильтр).
    /// </summary>
    Task<IReadOnlyList<Guid>?> GetVisibleBoardIdsAsync(Guid userId, CancellationToken cancellationToken);

    Task<BoardMember?> GetAsync(Guid boardId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BoardMember>> GetByBoardAsync(Guid boardId, CancellationToken cancellationToken);

    /// <summary>Группы проекта с их ролями, названием и числом участников.</summary>
    Task<IReadOnlyList<BoardGroupView>> GetGroupsByBoardAsync(Guid boardId, CancellationToken cancellationToken);

    Task<BoardGroup?> GetGroupAsync(Guid boardId, Guid groupId, CancellationToken cancellationToken);

    void AddGroup(BoardGroup group);

    void RemoveGroup(BoardGroup group);

    void Add(BoardMember member);

    void Remove(BoardMember member);
}
