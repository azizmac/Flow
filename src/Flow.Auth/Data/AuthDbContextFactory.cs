using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Flow.Auth.Data;

/// <summary>
/// Design-time фабрика для dotnet ef: не поднимает хост (а значит, не требует сертификатов и секретов),
/// строку подключения берёт из appsettings.json или env ConnectionStrings__Postgres.
/// </summary>
public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=flow;Username=flow;Password=flow";

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "auth"))
            .UseOpenIddict()
            .Options;

        return new AuthDbContext(options);
    }
}
