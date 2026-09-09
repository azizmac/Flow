using Flow.Auth.Data;
using Microsoft.EntityFrameworkCore;

namespace Flow.Auth.Security;

/// <summary>
/// При старте: миграции → клиенты и scope'ы OpenIddict → базовый пользователь. Один hosted service вместо трёх,
/// чтобы порядок был детерминирован (сидеры зависят от схемы).
/// </summary>
public sealed class AuthDatabaseInitializer(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        await provider.GetRequiredService<AuthDbContext>().Database.MigrateAsync(cancellationToken);
        await provider.GetRequiredService<ClientSeeder>().SeedAsync(cancellationToken);
        await provider.GetRequiredService<BootstrapUserSeeder>().SeedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
