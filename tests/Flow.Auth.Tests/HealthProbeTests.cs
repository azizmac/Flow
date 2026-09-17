using System.Net;

namespace Flow.Auth.Tests;

/// <summary>
/// Пробы Kubernetes должны отвечать анонимно. Во Flow.Auth глобального FallbackPolicy пока нет, но .AllowAnonymous()
/// на MapHealthChecks стоит явно — именно чтобы будущая политика не закрыла /health/* молча. Этот тест и держит
/// договорённость: если пробы начнут отдавать 401 или редирект на страницу входа, под никогда не станет Ready
/// и выкатка встанет, а без такой проверки CI останется зелёным — health-эндпоинты не трогает больше ни один тест.
/// Заодно проверяется, что ready виден снаружи после старта фабрики: хост поднят, значит миграции и сидеры позади,
/// а база (Testcontainers) доступна.
/// </summary>
[Collection(AuthCollection.Name)]
public sealed class HealthProbeTests(AuthFixture auth)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Probes_Should_Answer_200_Without_Token(string url)
    {
        // Клиент фикстуры не ходит по редиректам: 302 на /account/login тест увидит как провал, а не как успех.
        using var client = auth.CreateClient();

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Тело тоже проверяем: WriteMinimalPlaintext отдаёт одно слово, и «Healthy» отличает настоящую пробу
        // от случайного 200 какой-нибудь Razor-страницы с тем же кодом.
        Assert.Equal("Healthy", (await response.Content.ReadAsStringAsync()).Trim());
    }
}
