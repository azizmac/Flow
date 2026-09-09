using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Flow.Client.Services;

/// <summary>
/// Подставляет Bearer access token Flow.Auth во все запросы к Flow.Api (и только к нему — authorizedUrls).
/// Если токена нет или он истёк и не обновляется, бросает AccessTokenNotAvailableException — FlowApi ловит её
/// и отправляет на вход (ex.Redirect()).
/// </summary>
public sealed class FlowAuthorizationMessageHandler : AuthorizationMessageHandler
{
    public FlowAuthorizationMessageHandler(IAccessTokenProvider provider, NavigationManager navigation, string apiBaseUrl)
        : base(provider, navigation)
    {
        ConfigureHandler(authorizedUrls: [apiBaseUrl]);
    }
}
