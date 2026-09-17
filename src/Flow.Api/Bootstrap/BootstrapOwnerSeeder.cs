using Flow.Application.Features.Bootstrap;
using Flow.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flow.Api.Bootstrap;

/// <summary>
/// При старте: миграции FlowDbContext → профиль базового пользователя из секции Bootstrap (SeedBootstrapUserCommand,
/// идемпотентно). Миграции здесь же, потому что сидеру нужна схема, а в Docker больше некому их применять.
/// Роль Owner этому профилю отдаст миграция ролей (#17) как самому раннему по CreatedAt.
///
/// Флага готовности сидер больше не поднимает, и отдельного «а вдруг проба увидит нас недоделанными» бояться
/// не нужно: Kestrel (GenericWebHostService) регистрируется внутри builder.Build(), то есть после этого
/// hosted service, поэтому сокет не открывается, пока StartAsync не вернулся. Обратная сторона — до конца этого
/// метода не отвечает и /health/live, из-за чего манифест обязан объявлять startupProbe (см. комментарий
/// рядом с AddHealthChecks в Program.cs).
/// </summary>
public sealed class BootstrapOwnerSeeder(
    IServiceProvider services,
    IOptions<BootstrapOptions> options,
    IConfiguration configuration,
    ILogger<BootstrapOwnerSeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDbContext>();

        // База — в отдельном стеке Compose, ждать её healthcheck оттуда нельзя: ждём сами.
        await DatabaseReadiness.WaitAsync(db, DatabaseReadiness.TimeoutFrom(configuration), logger, cancellationToken);
        // Не db.Database.MigrateAsync: после миграции нужно перечитать каталог типов Npgsql — см. FlowDatabase.
        await FlowDatabase.MigrateAsync(scope.ServiceProvider, cancellationToken);

        var bootstrap = options.Value;
        // Ранний return вместо этого if был бы ошибкой: миграции выше нужны всегда, а выключенный сидер
        // (Bootstrap:Enabled=false) отменяет только создание профиля.
        if (bootstrap.Enabled)
        {
            var created = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
                new SeedBootstrapUserCommand(bootstrap.Id, bootstrap.Username, bootstrap.Email, bootstrap.FirstName, bootstrap.LastName),
                cancellationToken);

            if (created)
                logger.LogInformation("Bootstrap user profile {Username} created with id {Id}", bootstrap.Username, bootstrap.Id);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
