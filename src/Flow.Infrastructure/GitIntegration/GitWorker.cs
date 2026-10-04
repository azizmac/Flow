using Flow.Application.Features.GitIntegration;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.GitIntegration;

/// <summary>
/// Разбор доставок вебхуков (docs/TZ_git_integration.md §3): раз в Scm:PollIntervalSeconds берёт Pending-доставки, у
/// которых наступило NextAttemptAt, и разбирает каждую в своём scope — сбой одной не пачкает трекер следующей и
/// пишется отдельной командой (backoff 5 с → 5 мин, после 8 попыток — Failed). Раз в час удаляет обработанные
/// доставки старше срока хранения.
/// </summary>
internal sealed class GitWorker(IServiceScopeFactory scopes, GitOptions options, ILogger<GitWorker> logger) : BackgroundService
{
    private DateTime _lastPurge = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Проход разбора доставок Git завершился ошибкой.");
            }

            if (processed > 0)
                continue;
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            ids = await mediator.Send(new GitDueIntegrationJobsQuery(DateTime.UtcNow), cancellationToken);
            if (DateTime.UtcNow - _lastPurge > TimeSpan.FromHours(1))
            {
                _lastPurge = DateTime.UtcNow;
                await mediator.Send(new GitPurgeIntegrationJobsCommand(DateTime.UtcNow.AddDays(-options.DeliveryRetentionDays)), cancellationToken);
            }
        }

        foreach (var id in ids)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GitIntegrationJobProcessCommand(id), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Доставка Git {Delivery} не разобрана, повтор позже.", id);
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new GitIntegrationJobFailCommand(id, ex.Message), cancellationToken);
                }
                catch (Exception inner)
                {
                    logger.LogWarning(inner, "Не удалось записать сбой доставки {Delivery}.", id);
                }
            }
        }

        return ids.Count;
    }
}
