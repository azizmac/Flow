using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Queries.UserGetByUsernameQuery;

/// <summary>Регистр не важен: "Ilya" и "ilya" — один пользователь.</summary>
public sealed record UserGetByUsernameQuery(string Username) : IRequest<UserResponse?>;
