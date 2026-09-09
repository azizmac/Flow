using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardCreateCommand;

/// <summary>Result.IsKeyTaken = true, если доска с таким Key (после нормализации) уже существует.</summary>
/// <summary>ActorId — кто создаёт; право Admin+ (IPermissionService.EnsureCanManageBoards → 403).</summary>
public sealed record BoardCreateCommand(Guid ActorId, string Name, string Key) : IRequest<BoardCreateResult>;
