using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardCreateCommand;

public sealed record BoardCreateCommand(string Name, string Key) : IRequest<BoardResponse>;
