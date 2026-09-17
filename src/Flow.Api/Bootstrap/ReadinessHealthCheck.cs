using Flow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Flow.Api.Bootstrap;

/// <summary>
/// Проверка за <c>/health/ready</c> (тег ready). Содержательный вопрос здесь ровно один — доступна ли база
/// прямо сейчас: под, переживший падение Postgres, остаётся живым процессом, но обслуживать запросы не может,
/// и Kubernetes должен убрать его из Endpoints, а не гнать в него трафик.
///
/// Флага «стартовая инициализация завершена» здесь больше нет, и это не упрощение, а исправление ошибки.
/// GenericWebHostService (то есть сам Kestrel) регистрируется как hosted service внутри
/// <c>WebApplicationBuilder.Build()</c> — уже ПОСЛЕ всех <c>AddHostedService</c> из Program.cs, а хост запускает
/// hosted services строго в порядке регистрации. Значит сокет не открывается, пока <c>StartAsync</c>
/// у <see cref="BootstrapOwnerSeeder"/> не вернулся: проверено запуском Flow.Api с недоступной БД — за 25 секунд
/// ожидания в логе ноль строк «Now listening», curl на /health/live не устанавливает соединение вовсе.
/// К моменту, когда проба впервые получает хоть какой-то ответ, инициализация заведомо позади, и флаг всегда
/// был бы true — мёртвая ветка, которая создавала ложное ощущение защиты.
///
/// Unhealthy, а не Degraded — осознанный выбор, а не случайность. Раз readiness завязан на БД, то при моргании
/// Postgres под уходит из Endpoints и клиент получает 503 от Ingress вместо частично работающего API. Для одной
/// реплики это честнее: без базы Flow.Api не отдаст ни одной сущности, и «частично работающий» здесь означало бы
/// 500 на каждый запрос. Если реплик станет несколько, выбор стоит пересмотреть — тогда каскад дороже правды.
///
/// Наружу уходит только слово Healthy/Unhealthy: причина остаётся в отчёте для логов, а эндпоинт анонимный —
/// рассказывать миру о состоянии БД ему незачем.
/// </summary>
public sealed class ReadinessHealthCheck(FlowDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("database is not reachable");
}
