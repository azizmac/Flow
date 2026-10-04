using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardDeleteCommand;

/// <summary>
/// true — удалено, false — доска не найдена.
/// Обработчик - <see cref="BoardDeleteCommandHandler"/>
/// </summary>
public sealed record BoardDeleteCommand(Guid ActorId, Guid BoardId) : IRequest<bool>;
