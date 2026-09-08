using Flow.Application.DependencyInjection;
using Flow.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddFlowInfrastructure(builder.Configuration);
builder.Services.AddFlowApplication();
builder.Services.AddControllers();

var app = builder.Build();

app.MapGet("/", () => "Flow.Api");

app.MapControllers();

app.Run();
