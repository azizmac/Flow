using Flow.Shared.Ids;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardDeleteCommand;

/// <summary>true — удалено, false — доска не найдена.</summary>
public sealed record BoardDeleteCommand(BoardId BoardId) : IRequest<bool>;
