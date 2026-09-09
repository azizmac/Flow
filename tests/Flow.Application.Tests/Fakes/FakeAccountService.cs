using Flow.Application.Abstractions;

namespace Flow.Application.Tests.Fakes;

/// <summary>
/// Запоминает вызовы к Flow.Auth и отвечает тем, что настроено (по умолчанию — Success).
/// NextResult — одноразовый ответ на следующий вызов, возвращающий AccountResult (Create/ChangeUsername/ChangeEmail/ChangePassword).
/// </summary>
public sealed class FakeAccountService : IAccountService
{
    public sealed record Call(string Operation, Guid Id, string? Username = null, string? Email = null, string? Password = null, string? CurrentPassword = null);

    public List<Call> Calls { get; } = [];

    public AccountResult? NextResult { get; set; }

    /// <summary>Если задано — Disable/Enable бросают это исключение (недоступность Flow.Auth).</summary>
    public Exception? DisableEnableException { get; set; }

    public IEnumerable<Call> CallsTo(string operation) => Calls.Where(c => c.Operation == operation);

    public Task<AccountResult> CreateAsync(Guid id, string username, string email, string password, CancellationToken cancellationToken)
    {
        Calls.Add(new Call("Create", id, username, email, password));
        return Task.FromResult(Take());
    }

    public Task<AccountResult> ChangeUsernameAsync(Guid id, string username, CancellationToken cancellationToken)
    {
        Calls.Add(new Call("ChangeUsername", id, Username: username));
        return Task.FromResult(Take());
    }

    public Task<AccountResult> ChangeEmailAsync(Guid id, string email, CancellationToken cancellationToken)
    {
        Calls.Add(new Call("ChangeEmail", id, Email: email));
        return Task.FromResult(Take());
    }

    public Task<AccountResult> ChangePasswordAsync(Guid id, string? currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        Calls.Add(new Call("ChangePassword", id, Password: newPassword, CurrentPassword: currentPassword));
        return Task.FromResult(Take());
    }

    public Task DisableAsync(Guid id, CancellationToken cancellationToken)
    {
        Calls.Add(new Call("Disable", id));
        return DisableEnableException is null ? Task.CompletedTask : Task.FromException(DisableEnableException);
    }

    public Task EnableAsync(Guid id, CancellationToken cancellationToken)
    {
        Calls.Add(new Call("Enable", id));
        return DisableEnableException is null ? Task.CompletedTask : Task.FromException(DisableEnableException);
    }

    private AccountResult Take()
    {
        var result = NextResult ?? AccountResult.Success();
        NextResult = null;
        return result;
    }
}
