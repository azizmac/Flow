using Flow.Shared.Contracts.Users;

namespace Flow.Client.Services;

/// <summary>
/// Справочник групп для поля «Команда» и дорожек канбана (этап 4D): один GET /groups на circuit. Команды — группы с
/// IsTeam; прежние команды тоже в справочнике, чтобы у старых задач было имя. GroupsPanel сбрасывает его после правок.
/// </summary>
public sealed class TeamDirectory(IFlowApi api)
{
    private IReadOnlyList<GroupResponse>? _groups;

    public IReadOnlyList<GroupResponse> Teams => (_groups ?? []).Where(g => g.IsTeam).OrderBy(g => g.Name).ToList();

    public bool HasTeams => (_groups ?? []).Any(g => g.IsTeam);

    public GroupResponse? Find(Guid? id) => id is { } value ? _groups?.FirstOrDefault(g => g.Id == value) : null;

    public async Task EnsureLoadedAsync(bool force = false)
    {
        if (_groups is not null && !force)
            return;
        var result = await api.GetGroups();
        _groups = result.Ok ? result.Value! : [];
    }

    public void Invalidate() => _groups = null;
}
