using Microsoft.EntityFrameworkCore;

namespace Flow.Api.Bootstrap;

/// <summary>
/// Ожидание доступности Postgres перед миграциями. База живёт в отдельном стеке Compose
/// (docs/TZ_infra_data_split.md), межстекового depends_on у Compose нет — значит стеки поднимаются в любом
/// порядке, и сервис, стартовавший раньше базы, ждёт её сам. По истечении таймаута падаем с внятным
/// сообщением: restart-политика поднимет контейнер и цикл повторится.
/// </summary>
public static class DatabaseReadiness
{
    public const string TimeoutKey = "Startup:DatabaseWaitTimeoutSeconds";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    public static TimeSpan TimeoutFrom(IConfiguration configuration)
    {
        var seconds = configuration.GetValue<int?>(TimeoutKey);
        return seconds is > 0 ? TimeSpan.FromSeconds(seconds.Value) : DefaultTimeout;
    }

    public static async Task WaitAsync(DbContext context, TimeSpan timeout, ILogger logger, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var delay = FirstDelay;
        var attempt = 0;
        string? lastError = null;

        while (true)
        {
            attempt++;

            try
            {
                if (await context.Database.CanConnectAsync(cancellationToken))
                {
                    if (attempt > 1)
                        logger.LogInformation("Database is available (attempt {Attempt})", attempt);

                    return;
                }

                lastError = "connection refused";
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }

            if (DateTimeOffset.UtcNow + delay > deadline)
                throw new InvalidOperationException(
                    $"Database is not available after {timeout.TotalSeconds:0} s and {attempt} attempts: {lastError}. " +
                    $"Check that the data stack is running (docker compose -f docker-compose.data.yml up -d) " +
                    $"or that POSTGRES_HOST points to a reachable server.");

            logger.LogWarning("Database is not available (attempt {Attempt}): {Error}. Retrying in {Delay} s",
                attempt, lastError, delay.TotalSeconds);

            await Task.Delay(delay, cancellationToken);
            delay = delay < MaxDelay ? TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxDelay.Ticks)) : MaxDelay;
        }
    }
}
