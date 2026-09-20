using Flow.Api.Auth;
using Flow.Api.Bootstrap;
using Flow.Api.Client;
using Flow.Api.Components;
using Flow.Api.Routing;
using Flow.Application.Abstractions;
using Flow.Application.DependencyInjection;
using Flow.Auth.DependencyInjection;
using Flow.Client.DependencyInjection;
using Flow.Client.Layout;
using Flow.Client.Services;
using Flow.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;

// Флаг вырезается из args до создания builder'а намеренно: провайдер командной строки видит «--migrate»
// без «=» и забирает СЛЕДУЮЩИЙ аргумент себе как значение — строка подключения, переданная после него,
// до конфигурации не доезжала, и джоба молча шла в базу из appsettings. Поймано запуском, не рассуждением.
var migrateOnly = args.Contains("--migrate");
var builder = WebApplication.CreateBuilder(migrateOnly ? args.Where(a => a != "--migrate").ToArray() : args);

// Режим «только миграции»: `dotnet Flow.Api.dll --migrate` прогоняет ТЕ ЖЕ hosted-службы, что и обычный
// старт, и завершается. Именно те же — отдельной копии логики миграций не существует и разойтись ей не с чем.
// Зачем: пока миграции шли внутри веб-процесса, деплой обязан был быть Recreate (две реплики при
// RollingUpdate пошли бы мигрировать одновременно), а для серверного рендера это значит, что каждая
// выкатка рвёт все circuit'ы разом. Теперь их катит Job перед выкаткой, а веб-хост стартует на готовой
// схеме с Startup:RunMigrations=false.
if (migrateOnly)
{
    // Порт эфемерный и только на loopback: Kestrel в этом режиме поднимается лишь потому, что он
    // такая же hosted-служба, и гасится сразу. Слушать что-то наружу джобе незачем.
    builder.WebHost.UseUrls("http://127.0.0.1:0");
}

// Порядок AddHostedService = порядок старта: хост поднимает фоновые службы строго по очереди
// регистрации. Сначала мигрируют схемы (auth, затем public) и только потом стартует воркер
// индексации — он с первого прохода лезет в SearchIndexQueue, и на пустой базе до этой правки
// сыпал ошибками в лог, пока сидер не докатывал миграции.
builder.Services.Configure<BootstrapOptions>(builder.Configuration.GetSection(BootstrapOptions.SectionName));
builder.Services.AddAuthModule(builder.Configuration, builder.Environment); // 1. AuthDatabaseInitializer: схема auth
builder.Services.AddHostedService<BootstrapOwnerSeeder>();                  // 2. миграции public + профиль Owner
builder.Services.AddFlowInfrastructure(builder.Configuration);              // 3. SearchIndexingWorker: разбор очереди
// Поисковый индекс (секция "Search"): эмбеддер, очередь и фоновый воркер индексации.
// AddFlowInfrastructure уже вызывает его — повтор оставлен намеренно, чтобы состав сервисов
// читался прямо здесь; второй вызов ничего не регистрирует.
builder.Services.AddFlowSearch(builder.Configuration);
builder.Services.AddFlowApplication();
builder.Services.AddControllers(options =>
{
    options.Filters.Add<ApiExceptionFilter>();
    // Маршруты JSON-API уезжают под /api: иначе они сталкиваются со страницами интерфейса (см. ApiPrefixConvention).
    options.Conventions.Add(new ApiPrefixConvention("api"));
});
builder.Services.AddRazorPages();

// ---- Интерфейс Flow: серверный рендер (docs/TZ_client_mudblazor.md) ----
// Клиент перестал быть отдельным приложением WebAssembly и стал библиотекой компонентов этого хоста.
// Слабые машины пользователей были причиной перехода: браузер больше не выполняет .NET, он получает
// готовый HTML и дальше — патчи DOM по SignalR.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddFlowClientServices();
// Страницы ходят за данными через IFlowApi. Реализация живёт здесь, а не в Flow.Client: она
// обращается к Flow.Application напрямую, а клиент по слоям видит только Flow.Shared.
builder.Services.AddScoped<IFlowApi, InProcessFlowApi>();

// Ответы API — JSON с повторяющимися именами полей и hex-Guid'ами, сжимается в 6-9 раз:
// страница списка задач это десятки килобайт на каждое открытие и на каждую смену фильтра.
// "application/json" уже входит в ResponseCompressionDefaults.MimeTypes, добавлять его не нужно.
// EnableForHttps включён осознанно: тела ответов не содержат секретов (токен ездит в заголовке
// Authorization), поэтому BREACH к ним не применим.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

// Закрыто всё; исключения — явные [AllowAnonymous] (health, OIDC-эндпоинты, страница входа).
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IActorAccessor, ClaimsActorAccessor>();

// ---- Пробы Kubernetes: /health/live и /health/ready (docs/TZ_cicd_k8s.md §9 п.2) ----
// Две пробы, а не одна, потому что вопросы разные. live — «процесс жив», без единого обращения к БД:
// иначе упавший Postgres перезапускал бы по кругу заведомо исправный контейнер и чинить было бы нечего.
// ready — «можно слать трафик»; его набор проверок помечен тегом ready, живой набор пуст.
//
// ТРЕБОВАНИЯ К МАНИФЕСТУ — не пожелания, без них выкатка ломается на ровном месте:
//
// 1) startupProbe ОБЯЗАТЕЛЕН. GenericWebHostService (Kestrel) регистрируется внутри builder.Build() ниже,
//    то есть ПОСЛЕ AddHostedService<BootstrapOwnerSeeder>() выше, а хост стартует hosted services строго
//    по порядку регистрации. Значит порт вообще не слушается, пока сидер ждёт БД и катит миграции: отвечает
//    не 503, а connection refused — молчит и /health/ready, и /health/live (проверено запуском с недоступной
//    базой: за 25 секунд ни строки «Now listening», curl не устанавливает соединение). Дефолтный
//    livenessProbe (periodSeconds 10, failureThreshold 3) при таком старте убьёт контейнер посреди первой
//    миграции и будет делать это по кругу. Спасает только startupProbe: пока он не прошёл, kubelet не
//    запускает ни liveness, ни readiness. Его бюджет (failureThreshold × periodSeconds) обязан покрывать
//    Startup:DatabaseWaitTimeoutSeconds (по умолчанию 60 с, см. DatabaseReadiness) ПЛЮС время миграций,
//    с запасом на холодный старт узла.
//
// 2) timeoutSeconds у проб — не меньше 3. Внутренний таймаут проверки ниже равен 2 с, и он имеет смысл
//    только пока kubelet готов ждать дольше: с дефолтным timeoutSeconds: 1 он оборвёт сокет раньше, чем
//    проверка успеет вернуть честный 503, и в событиях пода вместо причины будет «probe timed out».
builder.Services.AddHealthChecks()
    // 2 с — верхняя граница ожидания ответа от БД, парная к timeoutSeconds ≥ 3 в манифесте (п. 2 выше).
    // Таймаут именно здесь: kubelet свой timeoutSeconds считает от сокета, а зависший запрос к БД
    // должен закончиться честным 503, а не удержанием пробы до её собственного таймаута.
    .AddCheck<ReadinessHealthCheck>("ready", tags: ["ready"], timeout: TimeSpan.FromSeconds(2));

// Интерфейс раздаётся этим же хостом, поэтому кросс-доменных запросов у него нет и CORS ему не нужен.
// Политика оставлена пустой и живой только ради отката на старый образ WASM-клиента: он ходил с другого
// origin. Список — секция "Cors:Origins" (env Cors__Origins__0); снимается вместе с ClientSeeder,
// когда серверный рендер отработает в проде.
const string clientCorsPolicy = "FlowClient";
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(clientCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// Сжатие — первым в конвейере, иначе статика и ответы контроллеров уйдут мимо него.
app.UseResponseCompression();

// wwwroot хоста (css/js/brand интерфейса переехали сюда из Flow.Client) и статика RCL-модулей.
// MapStaticAssets вместо UseStaticFiles: он сам выбирает предсжатое представление по Accept-Encoding,
// то есть отдаёт .br там, где nginx клиента умел только gzip. AllowAnonymous обязателен — FallbackPolicy
// ниже закрывает всё подряд, а MapStaticAssets это endpoint routing, в отличие от UseStaticFiles.
app.MapStaticAssets().AllowAnonymous();
app.UseCors(clientCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
// Обязателен для Razor Components; ставится после аутентификации.
app.UseAntiforgery();

// AllowAnonymous обязателен: FallbackPolicy выше закрывает всё подряд, а проба, получившая 401,
// означала бы «под не готов никогда». Ответ — одно слово Healthy/Unhealthy (WriteMinimalPlaintext по умолчанию).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

// Выход: гасим cookie сессии. Только POST — на GET чужая страница смогла бы разлогинить человека
// (cookie SameSite=Lax уходит при навигации верхнего уровня). Антифоржери-токен здесь не нужен и
// выключен намеренно: кросс-сайтовый POST cookie не донесёт, а кнопка выхода живёт внутри circuit'а,
// где токен формы взять неоткуда (её отправляет flow.submitPost).
app.MapPost("/account/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(IdentityConstants.ApplicationScheme);
    return Results.LocalRedirect("/");
}).DisableAntiforgery().AllowAnonymous();

app.MapRazorPages();
app.MapControllers();

// Страницы (@page) лежат в сборке Flow.Client, поэтому её надо назвать явно: сам по себе хост
// знает только про свои Components. Признак живости переехал на /health/live — прежний
// app.MapGet("/", () => "Flow.Api") удалён, иначе он дрался бы за "/" со страницей «Проекты».
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(MainLayout).Assembly);

if (migrateOnly)
{
    // StartAsync поднимает hosted-службы по порядку регистрации: схема auth, затем public и профиль
    // владельца. Исключение на этом же порядке и держится — падение любой из них валит джобу, что и нужно.
    await app.StartAsync();
    await app.StopAsync();
    return;
}

app.Run();

/// <summary>Для WebApplicationFactory в Flow.Api.Tests и Flow.Auth.Tests.</summary>
public partial class Program;
