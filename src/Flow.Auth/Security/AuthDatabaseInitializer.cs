using Flow.Auth.Data;
using Microsoft.EntityFrameworkCore;

namespace Flow.Auth.Security;

/// <summary>
/// При старте: миграции → клиенты и scope'ы OpenIddict → базовый пользователь. Один hosted service вместо трёх,
/// чтобы порядок был детерминирован (сидеры зависят от схемы).
///
/// Флага готовности инициализатор больше не поднимает: Kestrel (GenericWebHostService) регистрируется внутри
/// builder.Build(), то есть после этого hosted service, поэтому до возврата из StartAsync сокет не открыт и
/// увидеть нас недоделанными некому — ни одна проба не получит ответа. Обратная сторона — до конца этого метода
/// молчит и /health/live, из-за чего манифест обязан объявлять startupProbe (см. комментарий рядом
/// с AddHealthChecks в Program.cs).
/// </summary>
public sealed class AuthDatabaseInitializer(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<AuthDatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<AuthDbContext>();

        // База — в отдельном стеке Compose (docs/TZ_infra_data_split.md), ждать её healthcheck оттуда нельзя.
        await DatabaseReadiness.WaitAsync(db, DatabaseReadiness.TimeoutFrom(configuration), logger, cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await provider.GetRequiredService<ClientSeeder>().SeedAsync(cancellationToken);
        await provider.GetRequiredService<BootstrapUserSeeder>().SeedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
