using Flow.Shared.Contracts.Users;

namespace Flow.Application.Features.Users.Commands.UserCreateCommand;

/// <summary>Занятый username/email → 409, невалидный ввод → ArgumentException → 400 (по аналогии с BoardCreateResult).</summary>
public sealed class UserCreateResult
{
    public bool IsUsernameTaken { get; }

    public bool IsEmailTaken { get; }

    public bool IsConflict => IsUsernameTaken || IsEmailTaken;

    public string? ConflictError { get; }

    public UserResponse? Response { get; }

    private UserCreateResult(bool isUsernameTaken, bool isEmailTaken, string? conflictError, UserResponse? response)
    {
        IsUsernameTaken = isUsernameTaken;
        IsEmailTaken = isEmailTaken;
        ConflictError = conflictError;
        Response = response;
    }

    public static UserCreateResult UsernameTaken(string username) =>
        new(true, false, $"Username '{username}' is already in use.", null);

    public static UserCreateResult EmailTaken(string email) =>
        new(false, true, $"Email '{email}' is already in use.", null);

    public static UserCreateResult Success(UserResponse response) => new(false, false, null, response);
}
