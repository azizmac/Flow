using Flow.Api.Auth;
using Flow.Api.Bootstrap;
using Flow.Application.Abstractions;
using Flow.Application.DependencyInjection;
using Flow.Auth.DependencyInjection;
using Flow.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFlowInfrastructure(builder.Configuration);
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

// Flow.Client (Blazor WASM) хостится на другом origin (dev-сервер на :5016/:7062), поэтому браузеру нужен CORS.
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

app.MapGet("/", () => "Flow.Api").AllowAnonymous();

app.MapRazorPages();
app.MapControllers();

app.Run();

/// <summary>Для WebApplicationFactory в Flow.Api.Tests и Flow.Auth.Tests.</summary>
public partial class Program;
