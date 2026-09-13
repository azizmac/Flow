using Flow.Auth.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthModule(builder.Configuration, builder.Environment);
builder.Services.AddRazorPages();
builder.Services.AddControllers();

// Flow.Client ходит на discovery и /connect/token из браузера — нужен CORS на его origin'ы.
const string clientCorsPolicy = "FlowClient";
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(clientCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseStaticFiles();
app.UseCors(clientCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Flow.Auth");
app.MapRazorPages();
app.MapControllers();

app.Run();

/// <summary>Для WebApplicationFactory в Flow.Auth.Tests.</summary>
public partial class Program;
