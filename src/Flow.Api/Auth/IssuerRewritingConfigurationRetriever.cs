using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Flow.Api.Auth;

/// <summary>
/// Discovery Flow.Auth строит все адреса (jwks_uri и т.д.) от внешнего issuer (тот, что видит браузер, например
/// http://localhost:5100). Внутри Docker Flow.Api до него не достучится — ему доступен только внутренний адрес
/// (http://auth:8080). Поэтому документ и JWKS запрашиваются по внутреннему адресу, а issuer в токенах остаётся внешним.
/// Когда Issuer == BaseUrl (локальный запуск), переписывание — тождественное.
/// </summary>
public sealed class IssuerRewritingConfigurationRetriever(string externalIssuer, string internalBaseUrl)
    : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    private readonly string _external = externalIssuer.TrimEnd('/');
    private readonly string _internal = internalBaseUrl.TrimEnd('/');

    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var configuration = OpenIdConnectConfiguration.Create(await retriever.GetDocumentAsync(address, cancel));

        if (!string.IsNullOrEmpty(configuration.JwksUri))
        {
            var jwks = await retriever.GetDocumentAsync(Rewrite(configuration.JwksUri), cancel);
            configuration.JsonWebKeySet = new JsonWebKeySet(jwks);
            foreach (var key in configuration.JsonWebKeySet.GetSigningKeys())
                configuration.SigningKeys.Add(key);
        }

        return configuration;
    }

    private string Rewrite(string url) =>
        url.StartsWith(_external, StringComparison.OrdinalIgnoreCase) ? _internal + url[_external.Length..] : url;
}
