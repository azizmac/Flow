using Flow.Api.Auth;
using Flow.Api.Bootstrap;
using Flow.Application.Abstractions;
using Flow.Application.DependencyInjection;
using Flow.Auth.DependencyInjection;
using Flow.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFlowInfrastructure(builder.Configuration);
// Поисковый индекс (секция "Search"): эмбеддер, очередь и фоновый воркер индексации.
// AddFlowInfrastructure уже вызывает его — повтор оставлен намеренно, чтобы состав сервисов
// читался прямо здесь; второй вызов ничего не регистрирует.
builder.Services.AddFlowSearch(builder.Configuration);
builder.Services.AddFlowApplication();
builder.Services.AddAuthModule(builder.Configuration, builder.Environment);
builder.Services.AddControllers(options => options.Filters.Add<ApiExceptionFilter>());
builder.Services.AddRazorPages();

// Закрыто всё; исключения — явные [AllowAnonymous] (health, OIDC-эндпоинты, страница входа).
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IActorAccessor, ClaimsActorAccessor>();

builder.Services.Configure<BootstrapOptions>(builder.Configuration.GetSection(BootstrapOptions.SectionName));
builder.Services.AddHostedService<BootstrapOwnerSeeder>();

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

// Flow.Client (Blazor WASM) хостится на другом origin (dev-сервер на :5016), поэтому браузеру нужен CORS.
// Список origin'ов — секция "Cors:Origins" (appsettings / env Cors__Origins__0).
const string clientCorsPolicy = "FlowClient";
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(clientCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// wwwroot хоста и статика Auth-модуля (в dev — Static Web Assets, в publish — скопированный _content).
app.UseStaticFiles();
app.UseCors(clientCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

// AllowAnonymous обязателен: FallbackPolicy выше закрывает всё подряд, а проба, получившая 401,
// означала бы «под не готов никогда». Ответ — одно слово Healthy/Unhealthy (WriteMinimalPlaintext по умолчанию).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

app.MapGet("/", () => "Flow.Api").AllowAnonymous();

app.MapRazorPages();
app.MapControllers();

app.Run();

/// <summary>Для WebApplicationFactory в Flow.Api.Tests и Flow.Auth.Tests.</summary>
public partial class Program;
