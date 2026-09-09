using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserSetLinkCommand;

/// <summary>Добавляет ссылку или заменяет URL ссылки того же типа (см. User.SetLink).</summary>
public sealed record UserSetLinkCommand(Guid ActorId, Guid UserId, UserLinkType Type, string Url) : IRequest<UserUpdateResult>;
