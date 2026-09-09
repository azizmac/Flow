using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserRemoveLinkCommand;

/// <summary>Удаляет ссылку типа; если её не было — всё равно Success (идемпотентно).</summary>
public sealed record UserRemoveLinkCommand(Guid UserId, UserLinkType Type) : IRequest<UserUpdateResult>;
