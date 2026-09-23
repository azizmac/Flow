using System.Security.Claims;
using Flow.Shared.Contracts.Users;
using Microsoft.AspNetCore.Components.Authorization;

namespace Flow.Client.Services;

/// <summary>
/// Личные настройки интерфейса (GET/PATCH /users/me/preferences) на время circuit'а: грузятся один раз,
/// после сохранения на экране «Настройки» подменяются ответом сервера. Режим меню сразу уходит в
/// <see cref="AppState"/>, остальное экраны читают сами (размер страницы — список задач, стартовая — корень сайта).
/// </summary>
public sealed class PreferencesState(IFlowApi api, AppState state, AuthenticationStateProvider auth)
{
    /// <summary>Пока настройки не пришли (или не загрузились) — то же, что у нового пользователя на сервере.</summary>
    private const int DefaultTasksPageSize = 100;

    private Task? _loading;

    public UserPreferencesResponse? Current { get; private set; }

    public int TasksPageSize => Current?.TasksPageSize ?? DefaultTasksPageSize;

    public event Action? Changed;

    /// <summary>Один запрос на circuit; неудача не запоминается — следующий вызов попробует снова.</summary>
    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        var result = await api.GetPreferences();
        if (result.Ok)
            Apply(result.Value!);
        else
            _loading = null;
    }

    public async Task<ApiResult<UserPreferencesResponse>> UpdateAsync(UpdateUserPreferencesRequest request)
    {
        var result = await api.UpdatePreferences(request);
        if (result.Ok)
            Apply(result.Value!);

        return result;
    }

    /// <summary>
    /// Куда увести с корня сайта; null — остаться на «Проектах». «Мои задачи» — фильтр по себе, и id берём
    /// из сессии, а не из AppState: сайдбар кладёт его туда параллельно со страницей и может не успеть.
    /// </summary>
    public async Task<string?> StartPageHrefAsync()
    {
        await EnsureLoadedAsync();

        switch (Current?.StartPage)
        {
            case StartPage.Tasks:
                return "tasks";
            case StartPage.MyTasks:
                var user = (await auth.GetAuthenticationStateAsync()).User;
                var raw = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                return Guid.TryParse(raw, out var me) ? $"tasks?who={me}" : "tasks";
            default:
                return null;
        }
    }

    private void Apply(UserPreferencesResponse preferences)
    {
        Current = preferences;
        state.SetSidebarMode(preferences.SidebarMode);
        Changed?.Invoke();
    }
}
