using Flow.Application.Abstractions;
using Flow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.Repositories;

public sealed class RepositorySyncRecoveryService(IServiceProvider services, ILogger<RepositorySyncRecoveryService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDbContext>();
        var interrupted = await db.CodeRepositories
            .Where(repository => repository.SyncState == Flow.Domain.Entities.RepositorySyncState.Syncing)
            .ToListAsync(cancellationToken);

        foreach (var repository in interrupted)
            repository.FailSync("Предыдущая синхронизация была прервана. Запустите её повторно.");

        if (interrupted.Count == 0)
            return;

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
        logger.LogWarning("Recovered {Count} interrupted repository synchronizations", interrupted.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
