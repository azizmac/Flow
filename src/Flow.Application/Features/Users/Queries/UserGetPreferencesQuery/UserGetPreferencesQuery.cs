using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Queries.UserGetPreferencesQuery;

/// <summary>
/// Личные настройки текущего actor. В отличие от прочих запросов, actor здесь — не право, а сам предмет:
/// чужие настройки не читает никто. Нет профиля или деактивирован — 401 (ActorResolver).
/// </summary>
public sealed record UserGetPreferencesQuery(Guid ActorId) : IRequest<UserPreferencesResponse>;
