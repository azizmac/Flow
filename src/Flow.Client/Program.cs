using Flow.Client;
using Flow.Client.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Адрес Flow.Api — wwwroot/appsettings.json ("ApiBaseUrl"). По умолчанию — тот же origin, что и клиент.
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;
if (!apiBaseUrl.EndsWith('/'))
    apiBaseUrl += "/";

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
builder.Services.AddScoped<FlowApi>();
builder.Services.AddScoped<UserDirectory>();
builder.Services.AddScoped<BrowserInterop>();
builder.Services.AddScoped<HotkeyService>();
builder.Services.AddSingleton<ToastService>();
builder.Services.AddSingleton<AppState>();

await builder.Build().RunAsync();
