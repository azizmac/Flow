namespace Flow.Client.Services;

/// <summary>Состояние сессии клиента: последний открытый проект (для пункта «Задачи» в сайдбаре).</summary>
public sealed class AppState
{
    public Guid? LastBoardId { get; private set; }

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
