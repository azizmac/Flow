using Flow.Application.Abstractions;
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
    /// "ConnectionStrings:Postgres"), а также реализации репозиториев/UnitOfWork, объявленных
    /// в Flow.Application.Abstractions.
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
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
