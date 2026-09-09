using Flow.Domain.Entities;
using Flow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Перенос данных миграцией AddUserRoleAndStatus (#17): база доводится до предыдущей миграции, пользователи вставляются
/// SQL'ем со старыми колонками IsActive/DeactivatedAt, затем миграция применяется до конца. Свой контейнер, а не общая
/// коллекция Postgres: та уже мигрирована до последней версии.
/// </summary>
public sealed class RoleMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260909071433_AddTaskAssignee";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private FlowDbContext CreateContext() => new(new DbContextOptionsBuilder<FlowDbContext>()
        .UseNpgsql(_container.GetConnectionString())
        .Options);

    [Fact]
    public async Task Migration_Should_Map_IsActive_To_Status_And_Make_Earliest_User_Owner()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var gone = Guid.NewGuid();
        var deactivatedAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Users" ("Id", "Username", "Email", "FirstName", "LastName", "IsActive", "CreatedAt", "DeactivatedAt") VALUES
                ({0}, 'first',  'first@example.com',  'F', 'One',   TRUE,  '2026-08-01T10:00:00Z', NULL),
                ({1}, 'second', 'second@example.com', 'S', 'Two',   TRUE,  '2026-08-02T10:00:00Z', NULL),
                ({2}, 'gone',   'gone@example.com',   'G', 'Three', FALSE, '2026-08-03T10:00:00Z', {3});
                """, first, second, gone, deactivatedAt);

            await db.Database.MigrateAsync();
        }

        await using (var db = CreateContext())
        {
            var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id);

            Assert.Equal(UserRole.Owner, users[first].Role);
            Assert.Equal(UserRole.Member, users[second].Role);
            Assert.Equal(UserRole.Member, users[gone].Role);

            Assert.Equal(UserStatus.Active, users[first].Status);
            Assert.Equal(UserStatus.Active, users[second].Status);
            Assert.Equal(UserStatus.Deactivated, users[gone].Status);

            Assert.Null(users[first].StatusChangedAt);
            Assert.Equal(deactivatedAt, users[gone].StatusChangedAt);
            Assert.False(users[gone].IsActive);
            Assert.True(users[second].IsActive);
        }
    }

    [Fact]
    public async Task Migration_On_Empty_Users_Should_Succeed()
    {
        await using var db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        await db.Database.MigrateAsync();

        Assert.Equal(0, await db.Users.CountAsync());
        Assert.Contains("20260909174210_AddUserRoleAndStatus", await db.Database.GetAppliedMigrationsAsync());
    }
}
