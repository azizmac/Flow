using Flow.Shared.Contracts.Boards;
using Flow.Shared.Ids;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardRenameCommand;

/// <summary>Response = null, если доска не найдена.</summary>
public sealed record BoardRenameCommand(BoardId BoardId, string Name) : IRequest<BoardResponse?>;
