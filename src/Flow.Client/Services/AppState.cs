using Flow.Shared.Contracts.Users;

namespace Flow.Client.Services;

/// <summary>
/// Состояние сессии клиента: последний открытый проект (для пункта «Задачи» в сайдбаре) и текущий пользователь —
/// claim sub из токена Flow.Auth (ставит Sidebar после проверки сессии). Ручного выбора «Это я» больше нет.
/// </summary>
public sealed class AppState
{
    public Guid? LastBoardId { get; private set; }

    /// <summary>
    /// Иконочный режим сайдбара. null — «как решит ширина окна» (ниже 1366px он сворачивается сам,
    /// правило в app.css); true/false — выбор в настройках или кнопкой в меню, он важнее ширины.
    /// </summary>
    public bool? SidebarCollapsed { get; private set; }

    public void ToggleSidebar(bool collapsed)
    {
        SidebarCollapsed = collapsed;
        Changed?.Invoke();
    }

    /// <summary>Режим из личных настроек: с ним circuit стартует, кнопка в меню меняет его до перезагрузки.</summary>
    public void SetSidebarMode(SidebarMode mode)
    {
        bool? collapsed = mode switch
        {
            SidebarMode.Expanded => false,
            SidebarMode.Collapsed => true,
            _ => null
        };

        if (SidebarCollapsed == collapsed)
            return;

        SidebarCollapsed = collapsed;
        Changed?.Invoke();
    }

    public Guid? CurrentUserId { get; private set; }

    public void SetCurrentUser(Guid? id)
    {
        if (CurrentUserId == id)
            return;

        CurrentUserId = id;
        Changed?.Invoke();
    }

    public event Action? Changed;

    public void SetLastBoard(Guid id)
    {
        if (LastBoardId == id)
            return;

        LastBoardId = id;
        Changed?.Invoke();
    }

    public void ForgetBoard(Guid id)
    {
        if (LastBoardId != id)
            return;

        LastBoardId = null;
        Changed?.Invoke();
    }
}
