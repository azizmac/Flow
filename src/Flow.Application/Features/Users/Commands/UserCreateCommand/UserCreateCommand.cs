using MediatR;

namespace Flow.Application.Features.Users.Commands.UserCreateCommand;

/// <summary>Result.IsUsernameTaken / IsEmailTaken = true, если значение (после нормализации) уже занято.</summary>
public sealed record UserCreateCommand(string Username, string Email, string FirstName, string LastName)
    : IRequest<UserCreateResult>;
