using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardCreateCommand;

/// <summary>Result.IsKeyTaken = true, если доска с таким Key (после нормализации) уже существует.</summary>
public sealed record BoardCreateCommand(string Name, string Key) : IRequest<BoardCreateResult>;
