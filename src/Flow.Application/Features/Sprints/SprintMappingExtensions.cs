using Flow.Domain.Entities;
using Flow.Shared.Contracts.Sprints;
using SharedState = Flow.Shared.Contracts.Sprints.SprintState;

namespace Flow.Application.Features.Sprints;

public static class SprintMappingExtensions
{
    public static SprintResponse ToResponse(this Sprint sprint) => new(
        sprint.Id,
        sprint.BoardId,
        sprint.Name,
        sprint.Goal,
        sprint.StartDate,
        sprint.EndDate,
        (SharedState)(int)sprint.State,
        sprint.StartedAt,
        sprint.CompletedAt,
        sprint.SortOrder);
}
