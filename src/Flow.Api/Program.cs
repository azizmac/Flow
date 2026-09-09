using Flow.Application.DependencyInjection;
using Flow.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFlowInfrastructure(builder.Configuration);
builder.Services.AddFlowApplication();
builder.Services.AddControllers();

// Flow.Client (Blazor WASM) хостится на другом origin (dev-сервер на :5016/:7062), поэтому браузеру нужен CORS.
// Список origin'ов — секция "Cors:Origins" (appsettings / env Cors__Origins__0).
const string clientCorsPolicy = "FlowClient";
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(clientCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseCors(clientCorsPolicy);

app.MapGet("/", () => "Flow.Api");

app.MapControllers();

app.Run();
