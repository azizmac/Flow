using Flow.Application.Features.Tasks.Recurrence;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.Recurrence;

/// <summary>
/// Генератор повторяющихся задач (docs/TZ_task_model.md §9): раз в Recurrence:IntervalMinutes обходит активные правила,
/// каждое — в своём scope, как отдельный запрос: сорвавшееся правило не оставляет грязный трекер следующему.
/// Дубли при двух экземплярах хоста отсекает PK вхождения (правило, дата) — второй получит 23505 и пропустит правило.
/// </summary>
internal sealed class RecurrenceWorker(IServiceScopeFactory scopes, RecurrenceOptions options, ILogger<RecurrenceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.IntervalMinutes));
        logger.LogInformation("Генератор повторяющихся задач запущен, проход раз в {Interval}.", interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Проход генератора повторений завершился ошибкой.");
            }

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

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids;
        await using (var scope = scopes.CreateAsyncScope())
            ids = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RecurrenceActiveIdsQuery(), cancellationToken);

        var today = options.Today(DateTime.UtcNow);
        foreach (var id in ids)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var created = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RecurrenceGenerateCommand(id, today), cancellationToken);
                if (created > 0)
                    logger.LogInformation("Правило {Recurrence}: создано копий {Count}.", id, created);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Правило {Recurrence} не отработало, повтор на следующем проходе.", id);
                await RecordErrorAsync(id, ex.Message, cancellationToken);
            }
        }
    }

    private async Task RecordErrorAsync(Guid id, string message, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new RecurrenceRecordErrorCommand(id, message), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось записать ошибку правила {Recurrence}.", id);
        }
    }
}
