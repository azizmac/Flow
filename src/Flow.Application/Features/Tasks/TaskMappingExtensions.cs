using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Application.Features.Tasks;

public static class TaskMappingExtensions
{
    public static TaskResponse ToResponse(this TaskItem task) => new(
        task.Id,
        task.Code.Value,
        task.Title,
        task.Description,
        task.StatusId,
        task.AssigneeId,
        task.CreatedAt);
}
