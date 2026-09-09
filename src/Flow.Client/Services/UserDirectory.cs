using Flow.Shared.Contracts.Users;

namespace Flow.Client.Services;

/// <summary>
/// Кэш пользователей на сессию клиента: список задач показывает аватар исполнителя в каждой строке,
/// а TaskResponse несёт только AssigneeId — один GET /users?includeInactive=true вместо N GET /users/{id}.
/// Команда небольшая, поэтому держим всех (включая деактивированных: они остаются исполнителями старых задач).
/// </summary>
public sealed class UserDirectory(FlowApi api)
{
    private readonly Dictionary<Guid, UserResponse> _byId = new();
    private Task<ApiResult<IReadOnlyList<UserResponse>>>? _loading;
    private bool _loaded;

    public event Action? Changed;

    public bool IsLoaded => _loaded;

    /// <summary>Все известные пользователи: активные сначала, затем по имени.</summary>
    public IReadOnlyList<UserResponse> All => _byId.Values
        .OrderByDescending(u => u.IsActive)
        .ThenBy(u => u.FullName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public IReadOnlyList<UserResponse> Active => _byId.Values
        .Where(u => u.IsActive)
        .OrderBy(u => u.FullName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public UserResponse? Find(Guid? id) => id is { } i && _byId.TryGetValue(i, out var u) ? u : null;

    public UserResponse? FindByUsername(string username) =>
        _byId.Values.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

    /// <summary>Загружает справочник один раз; параллельные вызовы ждут один и тот же запрос.</summary>
    public async Task<ApiResult<IReadOnlyList<UserResponse>>> EnsureLoadedAsync(bool force = false)
    {
        if (_loaded && !force)
            return ApiResult<IReadOnlyList<UserResponse>>.Success(All, System.Net.HttpStatusCode.OK);

        _loading ??= api.GetUsers(includeInactive: true);
        var result = await _loading;
        _loading = null;

        if (result.Ok)
        {
            _byId.Clear();
            foreach (var u in result.Value!)
                _byId[u.Id] = u;
            _loaded = true;
            Changed?.Invoke();
        }

        return result;
    }

    /// <summary>Обновить/добавить пользователя после create/PATCH/activate — без перезагрузки списка.</summary>
    public void Put(UserResponse user)
    {
        _byId[user.Id] = user;
        Changed?.Invoke();
    }

    /// <summary>Локальный фильтр для выбора исполнителя: по @username, имени и фамилии, только активные.</summary>
    public IReadOnlyList<UserResponse> Filter(string query, int limit = 8)
    {
        var q = query.Trim().TrimStart('@');
        IEnumerable<UserResponse> users = Active;
        if (q.Length > 0)
            users = users.Where(u => u.Username.Contains(q, StringComparison.OrdinalIgnoreCase)
                                     || u.FirstName.Contains(q, StringComparison.OrdinalIgnoreCase)
                                     || u.LastName.Contains(q, StringComparison.OrdinalIgnoreCase)
                                     || u.FullName.Contains(q, StringComparison.OrdinalIgnoreCase));
        return users.Take(limit).ToList();
    }
}
