using Flow.Api.Bootstrap;
using Flow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Flow.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class BootstrapTests(ApiFixture api)
{
    [Fact]
    public async Task Host_Start_Should_Migrate_And_Seed_Bootstrap_Profile()
    {
        await using var scope = api.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDbContext>();

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == ApiFixture.BootstrapId);

        Assert.NotNull(user);
        Assert.Equal("admin", user!.Username);
        Assert.Equal("admin@flow.com", user.Email);
        Assert.True(user.IsActive);
        Assert.Empty(api.Accounts.CallsTo("Create").Where(c => c.Id == ApiFixture.BootstrapId));
    }

    [Fact]
    public async Task Seeder_Second_Run_Should_Not_Duplicate()
    {
        var seeder = api.Factory.Services.GetServices<IHostedService>().OfType<BootstrapOwnerSeeder>().Single();

        await seeder.StartAsync(CancellationToken.None);

        await using var scope = api.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Username == "admin"));
    }
}
