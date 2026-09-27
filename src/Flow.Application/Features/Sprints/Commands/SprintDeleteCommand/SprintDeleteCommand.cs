using MediatR;

namespace Flow.Application.Features.Sprints.Commands.SprintDeleteCommand;

/// <summary>
/// Удалить запланированный спринт; его задачи возвращаются в бэклог (SprintChanged у каждой). Активный и завершённый
/// не удаляются — это история (400). false — спринта нет. Права — ManageSprints.
/// </summary>
public sealed record SprintDeleteCommand(Guid ActorId, Guid SprintId) : IRequest<bool>;
