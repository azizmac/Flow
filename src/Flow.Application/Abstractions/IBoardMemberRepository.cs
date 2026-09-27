using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Данные для расчёта роли в проекте: потолок проекта и прямое участие человека.</summary>
public sealed record BoardAccessData(Guid BoardId, BoardVisibility Visibility, ProjectRole? DefaultRole, ProjectRole? MemberRole);

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

    void Add(BoardMember member);

    void Remove(BoardMember member);
}
