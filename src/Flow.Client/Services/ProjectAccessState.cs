using Flow.Shared.Contracts.Boards;

namespace Flow.Client.Services;

/// <summary>
/// Роль и права текущего пользователя в каждом проекте (docs/TZ_project_access.md, этап 4A): один
/// GET /boards/my-access на сессию, дальше — перезагрузка после смены участников и появления новых проектов.
/// Таблица «роль → права» живёт только на сервере: клиент прячет кнопки по списку прав, а не по роли.
/// Пока права не загружены, For() даёт null — и всё, что требует прав, скрыто (сервер всё равно проверит).
/// </summary>
public sealed class ProjectAccessState(IFlowApi api)
{
    private readonly Dictionary<Guid, ProjectAccessResponse> _byBoard = new();
    private Task<ApiResult<IReadOnlyList<ProjectAccessResponse>>>? _loading;
    private bool _loaded;

    public event Action? Changed;

    public ProjectAccessResponse? For(Guid? boardId) =>
        boardId is { } id && _byBoard.TryGetValue(id, out var access) ? access : null;

    public bool Can(Guid? boardId, ProjectPermission permission) => For(boardId)?.Permissions.Contains(permission) == true;

    /// <summary>Хоть в одном проекте есть право — для кнопки «Создать задачу» в сводном списке.</summary>
    public bool CanAnywhere(ProjectPermission permission) => _byBoard.Values.Any(a => a.Permissions.Contains(permission));

    public async Task EnsureLoadedAsync(bool force = false)
    {
        if (_loaded && !force)
            return;

        _loading ??= api.GetMyAccess();
        var result = await _loading;
        _loading = null;

        if (!result.Ok)
            return;

        _byBoard.Clear();
        foreach (var access in result.Value!)
            _byBoard[access.BoardId] = access;
        _loaded = true;
        Changed?.Invoke();
    }

    /// <summary>Проект появился после загрузки (создали только что, в соседней вкладке) — перечитать права.</summary>
    public Task EnsureCoversAsync(IEnumerable<Guid> boardIds) =>
        boardIds.All(_byBoard.ContainsKey) ? EnsureLoadedAsync() : EnsureLoadedAsync(force: true);
}
