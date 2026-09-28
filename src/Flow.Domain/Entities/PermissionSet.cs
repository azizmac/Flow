namespace Flow.Domain.Entities;

/// <summary>
/// Набор прав проекта (docs/TZ_project_access.md §7, этап 4E): именованный список <see cref="ProjectPermission"/>,
/// который выдают участнику или группе вместо встроенной роли. <see cref="BaseRole"/> — место набора на лестнице
/// ролей: по нему сравнивают «не выше своей», считают «последнего администратора» и максимум ролей. Встроенные
/// наборы — сами четыре роли, они живут в коде и не редактируются; свой набор делают клонированием. Базовая роль
/// после создания не меняется: она уже записана в участиях, которые на набор ссылаются.
/// </summary>
public sealed class PermissionSet
{
    public const int NameMaxLength = 60;
    public const int DescriptionMaxLength = 300;

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public ProjectRole BaseRole { get; private set; }

    private List<ProjectPermission> _permissions = [];

    public IReadOnlyList<ProjectPermission> Permissions => _permissions;

    public DateTime CreatedAt { get; private set; }

    private PermissionSet()
    {
        // EF Core
    }

    public static PermissionSet Create(string name, string? description, ProjectRole baseRole, IEnumerable<ProjectPermission> permissions)
    {
        if (!Enum.IsDefined(baseRole))
            throw new ArgumentException($"Unknown project role {baseRole}.", nameof(baseRole));
        var set = new PermissionSet { Id = Guid.NewGuid(), BaseRole = baseRole, CreatedAt = DateTime.UtcNow };
        set.Update(name, description, permissions);
        return set;
    }

    /// <summary>Имя, описание и права целиком. ViewProject есть всегда: набор без просмотра проекта бессмыслен.</summary>
    public void Update(string name, string? description, IEnumerable<ProjectPermission> permissions)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Название набора прав не может быть пустым.", nameof(name));
        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Название набора прав — не длиннее {NameMaxLength} символов.", nameof(name));
        var text = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (text?.Length > DescriptionMaxLength)
            throw new ArgumentException($"Описание набора — не длиннее {DescriptionMaxLength} символов.", nameof(description));

        var list = permissions.Append(ProjectPermission.ViewProject).Distinct().OrderBy(p => p).ToList();
        if (list.Any(p => !Enum.IsDefined(p)))
            throw new ArgumentException("Неизвестное право в наборе.", nameof(permissions));

        Name = trimmed;
        Description = text;
        _permissions = list;
    }
}
