using System.Net;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Пробы Kubernetes должны отвечать анонимно. В Flow.Api FallbackPolicy закрывает вообще всё, и единственное,
/// что отделяет /health/* от 401, — явный .AllowAnonymous() на MapHealthChecks. Убери его при рефакторинге —
/// и kubelet начнёт получать 401: startupProbe не пройдёт, под никогда не станет Ready, выкатка встанет
/// по progressDeadlineSeconds. Без этого теста CI при этом останется зелёным, потому что health-эндпоинты
/// не трогает больше ни один тест. Заодно проверяется, что ready виден снаружи после старта фабрики:
/// хост поднят, значит миграции позади и база (Testcontainers) доступна.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HealthProbeTests(ApiFixture api)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Probes_Should_Answer_200_Without_Token(string url)
    {
        using var client = api.CreateClient();

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Тело тоже проверяем: WriteMinimalPlaintext отдаёт одно слово, и «Healthy» отличает настоящую пробу
        // от случайного 200 какого-нибудь фолбэк-маршрута с тем же кодом.
        Assert.Equal("Healthy", (await response.Content.ReadAsStringAsync()).Trim());
    }
}
