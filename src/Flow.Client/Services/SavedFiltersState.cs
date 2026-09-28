using Flow.Shared.Contracts.Filters;

namespace Flow.Client.Services;

/// <summary>
/// Сохранённые фильтры на circuit (docs/TZ_task_views.md §7): один GET /filters, общий для сайдбара (избранные)
/// и экрана «Задачи» (меню фильтров). Правки идут через Put/Remove, событие Changed перерисовывает обоих.
/// </summary>
public sealed class SavedFiltersState(IFlowApi api)
{
    private List<SavedFilterResponse>? _filters;

    public event Action? Changed;

    public IReadOnlyList<SavedFilterResponse> All => _filters ?? [];

    public IReadOnlyList<SavedFilterResponse> Starred => All.Where(f => f.IsStarred).ToList();

    public async Task EnsureLoadedAsync(bool force = false)
    {
        if (_filters is not null && !force)
            return;

        var result = await api.GetFilters();
        _filters = result.Ok ? result.Value!.ToList() : [];
        Changed?.Invoke();
    }

    public SavedFilterResponse? Find(Guid? id) => id is null ? null : All.FirstOrDefault(f => f.Id == id);

    public void Put(SavedFilterResponse filter)
    {
        _filters ??= [];
        var i = _filters.FindIndex(f => f.Id == filter.Id);
        if (i >= 0) _filters[i] = filter;
        else _filters.Add(filter);
        _filters.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        Changed?.Invoke();
    }

    public void Remove(Guid id)
    {
        _filters?.RemoveAll(f => f.Id == id);
        Changed?.Invoke();
    }
}
