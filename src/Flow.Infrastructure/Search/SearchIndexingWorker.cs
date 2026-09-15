using Flow.Application.Features.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Фоновый цикл индексации внутри Flow.Api: каждые PollIntervalSeconds разбирает очередь, пока в ней
/// что-то есть. Отдельного хоста намеренно нет — это тот же процесс, тот же FlowDbContext; вынести цикл
/// в Flow.Worker можно позже без изменения кода, выставив Search:Indexing:Enabled=false в Api
/// (триггеры выноса — в разделе «Монолитность» базового ТЗ).
/// </summary>
internal sealed class SearchIndexingWorker(
    IServiceScopeFactory scopes,
    SearchOptions options,
    ILogger<SearchIndexingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Indexing.PollIntervalSeconds));
        logger.LogInformation("Индексация поиска запущена, опрос очереди раз в {Interval}.", interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            int processed;
            try
            {
                // Свой scope на проход — как отдельный запрос: свой DbContext и своя транзакция.
                await using var scope = scopes.CreateAsyncScope();
                var runner = scope.ServiceProvider.GetRequiredService<SearchIndexingRunner>();
                processed = await runner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Недоступная БД или модель не должны гасить воркер: ждём интервал и пробуем снова.
                logger.LogError(ex, "Проход индексации завершился ошибкой.");
                processed = 0;
            }

            // Очередь не пуста — сразу следующий батч, иначе холодный прогон растянется на интервалы сна.
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
}
