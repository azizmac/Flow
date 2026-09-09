using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Queries.UserGetMeQuery;

/// <summary>
/// Профиль текущего actor (claim sub из токена). null — учётная запись есть в Flow.Auth, а профиля нет (удалён руками):
/// контроллер отвечает 401. С ролями (#16) первый вызов переведёт Invited → Active.
/// </summary>
public sealed record UserGetMeQuery(Guid ActorId) : IRequest<UserResponse?>;
