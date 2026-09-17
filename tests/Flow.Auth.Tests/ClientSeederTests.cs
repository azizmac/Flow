using Flow.Auth.Security;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flow.Auth.Tests;

[Collection(AuthCollection.Name)]
public sealed class ClientSeederTests(AuthFixture auth)
{
    [Fact]
    public async Task Seed_Should_Delete_Legacy_Admin_Client_And_Scope()
    {
        await using var scope = auth.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        await applications.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = "flow-api",
            ClientSecret = "legacy-secret",
            ClientType = ClientTypes.Confidential,
            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.ClientCredentials,
                Permissions.Prefixes.Scope + "auth:admin"
            }
        });
        await scopes.CreateAsync(new OpenIddictScopeDescriptor
        {
            Name = "auth:admin",
            Resources = { "flow-auth" }
        });

        await scope.ServiceProvider.GetRequiredService<ClientSeeder>().SeedAsync(CancellationToken.None);

        Assert.Null(await applications.FindByClientIdAsync("flow-api"));
        Assert.Null(await scopes.FindByNameAsync("auth:admin"));
        Assert.NotNull(await applications.FindByClientIdAsync("flow-client"));
        Assert.NotNull(await scopes.FindByNameAsync(AuthConstants.ApiScope));
    }
}
