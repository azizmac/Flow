using Flow.Domain.Entities;
using Flow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Миграция AddUserPreferences на живой базе: у профилей, созданных до неё, настройки обязаны прочитаться
/// как у нового пользователя. Размер страницы 0 генератор ставит сам — его пришлось заменить на 100 руками,
/// и этот тест держит правку. Свой контейнер, как у RoleMigrationTests: общая коллекция уже мигрирована.
/// </summary>
public sealed class PreferencesMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260918201522_AddTaskTrigramIndexes";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private FlowDbContext CreateContext() => new(new DbContextOptionsBuilder<FlowDbContext>()
        .UseNpgsql(_container.GetConnectionString(), o => o.UseVector())
        .Options);

    [Fact]
    public async Task Existing_Users_Should_Get_Default_Preferences()
    {
        var id = Guid.NewGuid();

        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Users" ("Id", "Username", "Email", "FirstName", "LastName", "CreatedAt", "Role", "Status")
                VALUES ({0}, 'old', 'old@example.com', 'O', 'Ld', '2026-09-01T10:00:00Z', 2, 1);
                """, id);

            await db.Database.MigrateAsync();
        }

        await using (var db = CreateContext())
        {
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);

            Assert.Equal(SidebarMode.Auto, user.Preferences.SidebarMode);
            Assert.Equal(StartPage.Projects, user.Preferences.StartPage);
            Assert.Equal(UserPreferences.DefaultTasksPageSize, user.Preferences.TasksPageSize);
        }
    }
}
