using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardRenameCommand;

/// <summary>Response = null, если доска не найдена.</summary>
public sealed record BoardRenameCommand(Guid ActorId, Guid BoardId, string Name) : IRequest<BoardResponse?>;
