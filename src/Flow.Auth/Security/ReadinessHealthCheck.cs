using Flow.Auth.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Flow.Auth.Security;

/// <summary>
/// Проверка за <c>/health/ready</c> (тег ready). Содержательный вопрос здесь ровно один — доступна ли база
/// прямо сейчас: без БД Flow.Auth не выдаст ни одного токена, хотя процесс жив, и Kubernetes должен убрать
/// такой под из Endpoints, а не гнать в него трафик.
///
/// Флага «стартовая инициализация завершена» здесь больше нет, и это не упрощение, а исправление ошибки.
/// GenericWebHostService (то есть сам Kestrel) регистрируется как hosted service внутри
/// <c>WebApplicationBuilder.Build()</c> — уже ПОСЛЕ всех <c>AddHostedService</c> из Program.cs, а хост запускает
/// hosted services строго в порядке регистрации. Значит сокет не открывается, пока <c>StartAsync</c>
/// у <see cref="AuthDatabaseInitializer"/> не вернулся (миграции, клиенты OpenIddict, bootstrap-учётка):
/// проверено запуском с недоступной БД — за 25 секунд ожидания ноль строк «Now listening», соединение на
/// /health/live не устанавливается вовсе. К моменту, когда проба впервые получает хоть какой-то ответ,
/// инициализация заведомо позади, и флаг всегда был бы true — мёртвая ветка, создававшая ложное чувство защиты.
///
/// Unhealthy, а не Degraded — осознанный выбор, а не случайность. Раз readiness завязан на БД, то при моргании
/// Postgres под уходит из Endpoints и клиент получает 503 от Ingress вместо частично работающего сервиса входа.
/// Для одной реплики это честнее: без базы не отработает ни /connect/token, ни страница входа, и «частично
/// работающий» означало бы поток invalid_grant. Если реплик станет несколько, выбор стоит пересмотреть.
///
/// Наружу уходит только слово Healthy/Unhealthy: причина остаётся в отчёте для логов, а эндпоинт анонимный —
/// рассказывать миру о состоянии БД ему незачем.
/// </summary>
public sealed class ReadinessHealthCheck(AuthDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("database is not reachable");
}
