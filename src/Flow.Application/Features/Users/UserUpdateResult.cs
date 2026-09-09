using Flow.Shared.Contracts.Users;

namespace Flow.Application.Features.Users;

/// <summary>
/// Общий результат всех изменяющих команд пользователя: 404 (не найден), 409 (username/email занят) и 200
/// различаются HTTP-ответом, поэтому вместо исключений — явный результат (по аналогии с TaskUpdateResult).
/// Ошибки валидации самого ввода по-прежнему летят как ArgumentException/InvalidOperationException → 400.
/// </summary>
public sealed class UserUpdateResult
{
    public bool IsNotFound { get; }

    /// <summary>Заполнено при конфликте (занятый username/email) — контроллер отвечает 409.</summary>
    public string? ConflictError { get; }

    public UserResponse? Response { get; }

    private UserUpdateResult(bool isNotFound, string? conflictError, UserResponse? response)
    {
        IsNotFound = isNotFound;
        ConflictError = conflictError;
        Response = response;
    }

    public static UserUpdateResult NotFound() => new(true, null, null);

    public static UserUpdateResult UsernameTaken(string username) =>
        new(false, $"Username '{username}' is already in use.", null);

    public static UserUpdateResult EmailTaken(string email) =>
        new(false, $"Email '{email}' is already in use.", null);

    public static UserUpdateResult Success(UserResponse response) => new(false, null, response);
}
