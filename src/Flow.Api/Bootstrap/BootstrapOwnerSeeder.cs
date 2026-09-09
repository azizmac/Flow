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
/// </summary>
public sealed class BootstrapOwnerSeeder(IServiceProvider services, IOptions<BootstrapOptions> options, ILogger<BootstrapOwnerSeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FlowDbContext>().Database.MigrateAsync(cancellationToken);

        var bootstrap = options.Value;
        if (!bootstrap.Enabled)
            return;

        var created = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
            new SeedBootstrapUserCommand(bootstrap.Id, bootstrap.Username, bootstrap.Email, bootstrap.FirstName, bootstrap.LastName),
            cancellationToken);

        if (created)
            logger.LogInformation("Bootstrap user profile {Username} created with id {Id}", bootstrap.Username, bootstrap.Id);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
