using Flow.Application.Abstractions;

namespace Flow.Infrastructure.Tests;

/// <summary>Заглушка Flow.Auth для интеграционных тестов Flow.Infrastructure: любая операция успешна.</summary>
public sealed class AlwaysSucceedingAccountService : IAccountService
{
    public Task<AccountResult> CreateAsync(Guid id, string username, string email, string password, CancellationToken cancellationToken) =>
        Task.FromResult(AccountResult.Success());

    public Task<AccountResult> ChangeUsernameAsync(Guid id, string username, CancellationToken cancellationToken) =>
        Task.FromResult(AccountResult.Success());

    public Task<AccountResult> ChangeEmailAsync(Guid id, string email, CancellationToken cancellationToken) =>
        Task.FromResult(AccountResult.Success());

    public Task<AccountResult> ChangePasswordAsync(Guid id, string? currentPassword, string newPassword, CancellationToken cancellationToken) =>
        Task.FromResult(AccountResult.Success());

    public Task DisableAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task EnableAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
}
