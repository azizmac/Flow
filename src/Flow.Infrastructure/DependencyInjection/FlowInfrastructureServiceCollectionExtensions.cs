using Flow.Application.Abstractions;
using Flow.Infrastructure.Auth;
using Flow.Infrastructure.Persistence;
using Flow.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flow.Infrastructure.DependencyInjection;

public static class FlowInfrastructureServiceCollectionExtensions
{
    private const string PostgresConnectionStringName = "Postgres";

    /// <summary>
    /// Регистрирует Flow.Infrastructure: FlowDbContext на Npgsql (строка подключения — секция
    /// "ConnectionStrings:Postgres"), реализации репозиториев/UnitOfWork, объявленных
    /// в Flow.Application.Abstractions, и HTTP-клиент admin-API Flow.Auth (IAccountService, секция "Auth").
    /// Использование в Flow.Api/Program.cs: builder.Services.AddFlowInfrastructure(builder.Configuration);
    /// </summary>
    public static IServiceCollection AddFlowInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(PostgresConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string \"{PostgresConnectionStringName}\" is not configured.");

        services.AddDbContext<FlowDbContext>(options => options
            .UseNpgsql(connectionString));

        services.AddScoped<IBoardRepository, BoardRepository>();
        services.AddScoped<ITaskItemRepository, TaskItemRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITaskCommentRepository, TaskCommentRepository>();
        services.AddScoped<ITaskActivityRepository, TaskActivityRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Служебные вызовы в Flow.Auth (admin-API /accounts): секция "Auth" — BaseUrl и клиент flow-api.
        // Отсутствие настроек проявится AuthUnavailableException при первом вызове, а не на старте: тесты
        // Infrastructure подменяют IAccountService и в Flow.Auth не ходят.
        services.Configure<AuthClientOptions>(configuration.GetSection(AuthClientOptions.SectionName));
        services.AddHttpClient(ClientCredentialsTokenProvider.HttpClientName);
        services.AddSingleton<ClientCredentialsTokenProvider>();
        services.AddHttpClient<IAccountService, AuthAccountService>();

        return services;
    }
}
