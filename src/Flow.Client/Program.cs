using Flow.Client;
using Flow.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Адреса — wwwroot/appsettings.json: "ApiBaseUrl" (API) и "AuthBaseUrl" (OIDC authority того же backend).
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;
if (!apiBaseUrl.EndsWith('/'))
    apiBaseUrl += "/";

var authBaseUrl = builder.Configuration["AuthBaseUrl"]
    ?? throw new InvalidOperationException("AuthBaseUrl is not configured (wwwroot/appsettings.json).");

// Вход через Auth-модуль: Authorization Code + PKCE, refresh по offline_access. Redirect URI — authentication/login-callback,
// он же зарегистрирован у клиента flow-client (Auth:Client:RedirectUris). См. docs/TZ_modular_monolith.md.
builder.Services.AddOidcAuthentication(options =>
{
    options.ProviderOptions.Authority = authBaseUrl;
    options.ProviderOptions.ClientId = "flow-client";
    options.ProviderOptions.ResponseType = "code";
    options.ProviderOptions.DefaultScopes.Add("email");
    options.ProviderOptions.DefaultScopes.Add("offline_access");
    options.ProviderOptions.DefaultScopes.Add("flow-api");
    options.UserOptions.NameClaim = "name";
});

// HttpClient к Flow.Api с Bearer-токеном. FlowApi получает его через DI как обычный HttpClient.
builder.Services.AddScoped(sp => new FlowAuthorizationMessageHandler(
    sp.GetRequiredService<IAccessTokenProvider>(),
    sp.GetRequiredService<NavigationManager>(),
    apiBaseUrl));
builder.Services.AddHttpClient("FlowApi", client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<FlowAuthorizationMessageHandler>();
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("FlowApi"));

builder.Services.AddScoped<FlowApi>();
builder.Services.AddScoped<UserDirectory>();
builder.Services.AddScoped<BrowserInterop>();
builder.Services.AddScoped<HotkeyService>();
builder.Services.AddSingleton<ToastService>();
builder.Services.AddSingleton<AppState>();
// Один на приложение: блок вложений и редактор комментария узнают о новых файлах друг друга.
builder.Services.AddSingleton<AttachmentEvents>();

await builder.Build().RunAsync();
