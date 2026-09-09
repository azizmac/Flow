namespace Flow.Client.Services;

/// <summary>
/// Состояние сессии клиента: последний открытый проект (для пункта «Задачи» в сайдбаре) и «это я» —
/// профиль в футере сайдбара. Аутентификации в API пока нет, поэтому «я» выбирается вручную на странице
/// пользователя и хранится в localStorage (ключ flow.me), см. BrowserInterop.
/// </summary>
public sealed class AppState
{
    public Guid? LastBoardId { get; private set; }

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
