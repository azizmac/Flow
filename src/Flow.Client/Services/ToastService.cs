using Flow.Client.Components;
using MudBlazor;

namespace Flow.Client.Services;

public enum ToastKind { Info, Ok, Error }

/// <summary>
/// Короткие уведомления (ошибки API, «скопировано»). Очередь, таймеры и анимации — за MudBlazor
/// (<see cref="ISnackbar"/>, провайдер поднят в App.razor); здесь остаётся словарь Flow: наши глифы,
/// наши длительности и класс .toast, по которому вид настроен в mud-overrides.css.
/// Сервис сохранён как фасад намеренно — вызовов Toasts.Ok/Error по коду семь десятков.
/// </summary>
public sealed class ToastService(ISnackbar snackbar)
{
    /// <summary>Ошибку читают дольше, чем подтверждение.</summary>
    private const int InfoMs = 2500;
    private const int ErrorMs = 5000;

    public void Show(string text, ToastKind kind = ToastKind.Info) =>
        snackbar.Add(text, Severity(kind), options =>
        {
            options.VisibleStateDuration = kind == ToastKind.Error ? ErrorMs : InfoMs;
            options.Icon = kind == ToastKind.Error ? FlowIcons.Glyphs["danger"] : FlowIcons.Glyphs["check"];
            options.SnackbarTypeClass = kind switch
            {
                ToastKind.Error => "toast error",
                ToastKind.Ok => "toast ok",
                _ => "toast"
            };
        });

    public void Error(string text) => Show(text, ToastKind.Error);

    public void Ok(string text) => Show(text, ToastKind.Ok);

    private static Severity Severity(ToastKind kind) => kind switch
    {
        ToastKind.Error => MudBlazor.Severity.Error,
        ToastKind.Ok => MudBlazor.Severity.Success,
        _ => MudBlazor.Severity.Normal
    };
}
