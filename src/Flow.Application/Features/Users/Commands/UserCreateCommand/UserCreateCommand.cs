using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserCreateCommand;

/// <summary>
/// Создаёт учётную запись в Flow.Auth (username, email, начальный пароль) и профиль в Users с тем же Id.
/// Result.IsUsernameTaken / IsEmailTaken = true, если значение (после нормализации) уже занято — локально или в Flow.Auth.
/// Слабый пароль или иной отказ Flow.Auth → ArgumentException → 400.
/// </summary>
/// <remarks>ActorId — Admin+; Role по умолчанию Member; Admin не может выдать Admin/Owner (403).</remarks>
public sealed record UserCreateCommand(Guid ActorId, string Username, string Email, string FirstName, string LastName, string Password, UserRole? Role = null)
    : IRequest<UserCreateResult>;
