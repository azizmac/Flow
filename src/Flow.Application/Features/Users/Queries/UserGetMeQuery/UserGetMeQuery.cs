using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Queries.UserGetMeQuery;

/// <summary>
/// Профиль текущего actor (claim sub из токена). null — учётная запись есть в Flow.Auth, а профиля нет (удалён руками):
/// контроллер отвечает 401. Первый вызов после входа переводит Invited → Active.
/// </summary>
public sealed record UserGetMeQuery(Guid ActorId) : IRequest<UserResponse?>;
