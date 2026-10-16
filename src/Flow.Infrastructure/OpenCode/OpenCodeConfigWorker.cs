using Flow.Application.Features.Agents.Settings;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.OpenCode;

/// <summary>
/// Держит OpenCode в согласии с «Настройки → Агент»: на старте передаёт провайдеров и ключи целиком, дальше раз в
/// OpenCode:SyncIntervalSeconds проверяет, что OpenCode их видит, и передаёт заново, если контейнер пересоздали
/// (ключи у него живут без постоянного тома) или прошлая передача не дошла. Перезапускать что-либо руками не нужно.
/// </summary>
internal sealed class OpenCodeConfigWorker(IServiceScopeFactory scopes, OpenCodeOptions options, ILogger<OpenCodeConfigWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, options.SyncIntervalSeconds));
        var force = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new AgentSyncCommand(force), stoppingToken))
                    logger.LogInformation("Провайдеры и ключи агента переданы в OpenCode.");
                force = false;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Синхронизация настроек агента с OpenCode не удалась.");
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
}
