using Flow.Auth.Options;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flow.Auth.Security;

/// <summary>
/// Регистрирует scope'ы и двух клиентов OpenIddict из конфигурации. Идемпотентен: существующие записи
/// обновляются, поэтому смена redirect URI или секрета в конфиге подхватывается перезапуском.
/// </summary>
public sealed class ClientSeeder(
    IOpenIddictApplicationManager applications,
    IOpenIddictScopeManager scopes,
    IOptions<AuthOptions> options,
    ILogger<ClientSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var auth = options.Value;

        await UpsertScopeAsync(AuthConstants.ApiScope, "Flow API", AuthConstants.ApiResource, cancellationToken);
        await UpsertScopeAsync(AuthConstants.AdminScope, "Flow.Auth admin API", AuthConstants.AuthResource, cancellationToken);

        var client = new OpenIddictApplicationDescriptor
        {
            ClientId = auth.Client.ClientId,
            ClientType = ClientTypes.Public,
            ApplicationType = ApplicationTypes.Web,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "Flow",
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Prefixes.Scope + AuthConstants.ApiScope
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange }
        };

        foreach (var uri in auth.Client.RedirectUris)
            client.RedirectUris.Add(new Uri(uri, UriKind.Absolute));

        foreach (var uri in auth.Client.PostLogoutRedirectUris)
            client.PostLogoutRedirectUris.Add(new Uri(uri, UriKind.Absolute));

        await UpsertApplicationAsync(client, cancellationToken);

        if (string.IsNullOrWhiteSpace(auth.ApiClient.Secret))
            throw new InvalidOperationException("Auth:ApiClient:Secret is not configured — Flow.Api cannot call the admin API without it.");

        var api = new OpenIddictApplicationDescriptor
        {
            ClientId = auth.ApiClient.ClientId,
            ClientSecret = auth.ApiClient.Secret,
            ClientType = ClientTypes.Confidential,
            DisplayName = "Flow.Api",
            Permissions =
            {
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.ClientCredentials,
                Permissions.Prefixes.Scope + AuthConstants.AdminScope
            }
        };

        await UpsertApplicationAsync(api, cancellationToken);
    }

    private async Task UpsertApplicationAsync(OpenIddictApplicationDescriptor descriptor, CancellationToken cancellationToken)
    {
        var existing = await applications.FindByClientIdAsync(descriptor.ClientId!, cancellationToken);
        if (existing is null)
        {
            await applications.CreateAsync(descriptor, cancellationToken);
            logger.LogInformation("OpenIddict client {ClientId} created", descriptor.ClientId);
        }
        else
        {
            await applications.UpdateAsync(existing, descriptor, cancellationToken);
        }
    }

    private async Task UpsertScopeAsync(string name, string displayName, string resource, CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = name,
            DisplayName = displayName,
            Resources = { resource }
        };

        var existing = await scopes.FindByNameAsync(name, cancellationToken);
        if (existing is null)
            await scopes.CreateAsync(descriptor, cancellationToken);
        else
            await scopes.UpdateAsync(existing, descriptor, cancellationToken);
    }
}
