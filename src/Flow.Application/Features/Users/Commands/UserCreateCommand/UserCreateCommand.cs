using MediatR;

namespace Flow.Application.Features.Users.Commands.UserCreateCommand;

/// <summary>
/// Создаёт учётную запись в Flow.Auth (username, email, начальный пароль) и профиль в Users с тем же Id.
/// Result.IsUsernameTaken / IsEmailTaken = true, если значение (после нормализации) уже занято — локально или в Flow.Auth.
/// Слабый пароль или иной отказ Flow.Auth → ArgumentException → 400.
/// </summary>
public sealed record UserCreateCommand(string Username, string Email, string FirstName, string LastName, string Password)
    : IRequest<UserCreateResult>;
