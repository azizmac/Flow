using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using DomainStatusType = Flow.Domain.Entities.StatusType;
using SharedStatusType = Flow.Shared.Contracts.Boards.StatusType;

namespace Flow.Application.Features.Boards;

public static class BoardMappingExtensions
{
    public static BoardResponse ToResponse(this Board board) => new(
        board.Id,
        board.Key,
        board.Name,
        board.CreatedAt,
        board.Statuses
            .OrderBy(s => s.SortOrder)
            .Select(s => new StatusResponse(s.Id, s.Name, s.IsInitial, s.IsFinal, s.Type.ToResponseStatusType()))
            .ToList());

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
