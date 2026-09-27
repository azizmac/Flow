using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using DomainStatusType = Flow.Domain.Entities.StatusType;
using SharedStatusType = Flow.Shared.Contracts.Boards.StatusType;
using DomainTypeKind = Flow.Domain.Entities.TaskTypeKind;
using SharedTypeKind = Flow.Shared.Contracts.Boards.TaskTypeKind;
using DomainProjectRole = Flow.Domain.Entities.ProjectRole;
using SharedProjectRole = Flow.Shared.Contracts.Boards.ProjectRole;
using DomainPermission = Flow.Domain.Entities.ProjectPermission;
using SharedPermission = Flow.Shared.Contracts.Boards.ProjectPermission;
using Flow.Application.Security;

namespace Flow.Application.Features.Boards;

public static class BoardMappingExtensions
{
    /// <summary>
    /// <paramref name="taskCount"/> передаётся снаружи: Board.Tasks в запросах не подгружается
    /// (см. BoardRepository), а счётчик считается отдельным GROUP BY через ITaskItemRepository.CountByBoardIdsAsync.
    /// </summary>
    public static BoardResponse ToResponse(this Board board, int taskCount) => new(
        board.Id,
        board.Key,
        board.Name,
        board.CreatedAt,
        taskCount,
        board.NextTaskNumber + 1,
        board.Statuses
            .OrderBy(s => s.SortOrder)
            .Select(s => new StatusResponse(s.Id, s.Name, s.IsInitial, s.IsFinal, s.Type.ToResponseStatusType()))
            .ToList(),
        board.TaskTypes
            .OrderBy(t => t.SortOrder)
            .Select(t => t.ToResponse())
            .ToList(),
        board.DefaultRole?.ToResponseRole());

    public static BoardMembersResponse ToMembersResponse(this Board board, IEnumerable<BoardMember> members) => new(
        board.Id,
        board.DefaultRole?.ToResponseRole(),
        members
            .OrderByDescending(m => m.Role)
            .ThenBy(m => m.AddedAt)
            .Select(m => new BoardMemberResponse(m.UserId, m.Role.ToResponseRole(), m.AddedById, m.AddedAt))
            .ToList());

    public static ProjectAccessResponse ToResponse(this ProjectAccessInfo access) => new(
        access.BoardId,
        access.Role.ToResponseRole(),
        access.Permissions.OrderBy(p => p).Select(p => p.ToResponsePermission()).ToList());

    public static SharedProjectRole ToResponseRole(this DomainProjectRole role) =>
        Enum.IsDefined(role) ? (SharedProjectRole)(int)role : throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown ProjectRole.");

    public static DomainProjectRole ToDomainRole(this SharedProjectRole role) =>
        Enum.IsDefined(role) ? (DomainProjectRole)(int)role : throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown ProjectRole.");

    private static SharedPermission ToResponsePermission(this DomainPermission permission) =>
        Enum.IsDefined(permission) ? (SharedPermission)(int)permission : throw new ArgumentOutOfRangeException(nameof(permission), permission, "Unknown ProjectPermission.");

    public static TaskTypeResponse ToResponse(this TaskType type) =>
        new(type.Id, type.Name, type.Kind.ToResponseKind(), type.Level, type.IsDefault, type.IsArchived);

    /// <summary>Зеркала с одинаковыми значениями (Shared не ссылается на Domain) — приведение с проверкой.</summary>
    public static SharedTypeKind ToResponseKind(this DomainTypeKind kind) =>
        Enum.IsDefined(kind) ? (SharedTypeKind)(int)kind : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown TaskTypeKind.");

    public static DomainTypeKind ToDomainKind(this SharedTypeKind kind) =>
        Enum.IsDefined(kind) ? (DomainTypeKind)(int)kind : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown TaskTypeKind.");

    /// <summary>
    /// Явный маппинг вместо приведения типов: Flow.Domain.Entities.StatusType и
    /// Flow.Shared.Contracts.Boards.StatusType — два разных enum (Flow.Shared намеренно не ссылается на Flow.Domain).
    /// </summary>
    private static SharedStatusType? ToResponseStatusType(this DomainStatusType? type) => type switch
    {
        null => null,
        DomainStatusType.NotStarted => SharedStatusType.NotStarted,
        DomainStatusType.InProgress => SharedStatusType.InProgress,
        DomainStatusType.InReview => SharedStatusType.InReview,
        DomainStatusType.Done => SharedStatusType.Done,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown StatusType.")
    };
}
