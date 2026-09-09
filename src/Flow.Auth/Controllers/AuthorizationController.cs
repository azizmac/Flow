using System.Security.Claims;
using Flow.Auth.Data;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flow.Auth.Controllers;

/// <summary>
/// Эндпоинты OpenIddict в passthrough-режиме: OpenIddict валидирует запрос (client_id, redirect_uri, PKCE, код),
/// а здесь решается, кто входит и какие claims попадут в токены. Роль и статус workspace сюда намеренно не
/// попадают — их Flow.Api читает из своей БД на каждую команду (см. docs/TZ_auth.md).
/// </summary>
public sealed class AuthorizationController(
    UserManager<ApplicationUser> users,
    IOpenIddictApplicationManager applications,
    IOpenIddictScopeManager scopes) : Controller
{
    private const string IgnoreChallengeKey = "IgnoreAuthenticationChallenge";

    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        // Cookie ставит страница /account/login. Нет cookie или клиент попросил prompt=login → на вход.
        // TempData-флаг защищает от петли login → authorize → login при prompt=login.
        var result = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (result is not { Succeeded: true }
            || (request.HasPromptValue(PromptValues.Login) && TempData[IgnoreChallengeKey] is not true))
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The user is not logged in."
                    }));
            }

            TempData[IgnoreChallengeKey] = true;

            return Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
                        Request.HasFormContentType ? Request.Form : Request.Query)
                },
                IdentityConstants.ApplicationScheme);
        }

        var user = await users.GetUserAsync(result.Principal);
        if (user is null || await users.IsLockedOutAsync(user))
        {
            // Деактивирован после того, как cookie была выдана: гасим cookie и отказываем.
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return Forbid(
                authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                properties: new AuthenticationProperties(new Dictionary<string, string?>
                {
                    [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.AccessDenied,
                    [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Доступ закрыт."
                }));
        }

        var identity = await CreateIdentityAsync(user);
        identity.SetScopes(request.GetScopes());
        identity.SetResources(await scopes.ListResourcesAsync(identity.GetScopes()).ToListAsync());
        identity.SetDestinations(GetDestinations);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/connect/token")]
    [IgnoreAntiforgeryToken]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
        {
            // Principal из кода / refresh-токена уже проверен OpenIddict. Пользователя перечитываем:
            // деактивированный (lockout) не должен получить новые токены.
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var subject = result.Principal?.GetClaim(Claims.Subject);
            var user = subject is null ? null : await users.FindByIdAsync(subject);

            if (user is null || await users.IsLockedOutAsync(user))
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.InvalidGrant,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The token is no longer valid."
                    }));
            }

            var principal = result.Principal!;
            var identity = await CreateIdentityAsync(user);
            identity.SetScopes(principal.GetScopes());
            identity.SetResources(principal.GetResources());
            identity.SetAuthorizationId(principal.GetAuthorizationId());
            identity.SetDestinations(GetDestinations);

            return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType())
        {
            // client_id/client_secret уже проверены OpenIddict — сюда попадает только flow-api.
            var application = await applications.FindByClientIdAsync(request.ClientId!)
                ?? throw new InvalidOperationException("The application cannot be found.");

            var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
            identity.SetClaim(Claims.Subject, await applications.GetClientIdAsync(application));
            identity.SetClaim(Claims.Name, await applications.GetDisplayNameAsync(application));
            identity.SetScopes(request.GetScopes());
            identity.SetResources(await scopes.ListResourcesAsync(identity.GetScopes()).ToListAsync());
            identity.SetDestinations(GetDestinations);

            return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new InvalidOperationException("The specified grant type is not supported.");
    }

    [HttpGet("~/connect/endsession")]
    [HttpPost("~/connect/endsession")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> EndSession()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);

        // OpenIddict сам подставит post_logout_redirect_uri из запроса, если он зарегистрирован у клиента.
        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static Task<ClaimsIdentity> CreateIdentityAsync(ApplicationUser user)
    {
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id.ToString())
            .SetClaim(Claims.Name, user.UserName)
            .SetClaim(Claims.PreferredUsername, user.UserName)
            .SetClaim(Claims.Email, user.Email);

        return Task.FromResult(identity);
    }

    /// <summary>
    /// sub попадает всюду по умолчанию. name/preferred_username/email — в access и id token при соответствующих
    /// scope (access token нужен для userinfo и для Flow.Api), всё остальное — только в access token.
    /// </summary>
    private static IEnumerable<string> GetDestinations(Claim claim) => claim.Type switch
    {
        Claims.Name or Claims.PreferredUsername when claim.Subject?.HasScope(Scopes.Profile) == true
            => [Destinations.AccessToken, Destinations.IdentityToken],

        Claims.Email when claim.Subject?.HasScope(Scopes.Email) == true
            => [Destinations.AccessToken, Destinations.IdentityToken],

        _ => [Destinations.AccessToken]
    };
}
