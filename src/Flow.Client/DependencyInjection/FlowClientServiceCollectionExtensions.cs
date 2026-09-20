using Flow.Client.Services;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Flow.Client.DependencyInjection;

public static class FlowClientServiceCollectionExtensions
{
    /// <summary>
    /// Сервисы интерфейса Flow. Переехали сюда из Program.cs standalone-приложения WebAssembly:
    /// теперь их регистрирует хост (Flow.Api). Реализацию <see cref="IFlowApi"/> здесь НЕ регистрируем —
    /// её подставляет хост, потому что она ходит в Flow.Application, а Flow.Client его не видит.
    /// </summary>
    public static IServiceCollection AddFlowClientServices(this IServiceCollection services)
    {
        // UI-кит: поповеры, диалоги, снекбары, MudDataGrid. Тема — Services/FlowTheme.cs, провайдеры — Components/Routes.razor.
        // Уведомления Flow выходят снизу по центру и закрываются сами: кнопку закрытия не показываем,
        // длительности и иконки каждому тосту проставляет ToastService.
        services.AddMudServices(options =>
        {
            options.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomCenter;
            options.SnackbarConfiguration.NewestOnTop = false;
            options.SnackbarConfiguration.ShowCloseIcon = false;
            options.SnackbarConfiguration.PreventDuplicates = false;
            options.SnackbarConfiguration.ShowTransitionDuration = 220;
            options.SnackbarConfiguration.HideTransitionDuration = 180;
            // Глиф в тосте 15px, как было у своего компонента, и без полупрозрачности Material.
            options.SnackbarConfiguration.IconSize = MudBlazor.Size.Small;
            options.SnackbarConfiguration.MaximumOpacity = 100;
        });

        services.AddScoped<UserDirectory>();
        services.AddScoped<BrowserInterop>();
        services.AddScoped<HotkeyService>();
        // Scoped, а не Singleton: внутри лежит ISnackbar, который сам scoped.
        services.AddScoped<ToastService>();

        // Раньше это были Singleton, и в standalone WebAssembly разницы не было — корневой scope один
        // на приложение. На сервере разница принципиальная: один экземпляр на всех означал бы, что
        // AppState.CurrentUserId перетирается между сессиями (а на нём держится вся матрица прав в
        // Permissions), а событие AttachmentEvents.Changed уходило бы всем, у кого открыта та же задача.
        services.AddScoped<AppState>();
        services.AddScoped<AttachmentEvents>();
        // Размеры картинок-вложений: их узнаёт список вложений, а нужны они рендеру Markdown.
        services.AddScoped<AttachmentSizes>();

        return services;
    }
}
