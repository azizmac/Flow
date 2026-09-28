namespace Flow.Domain.Entities;

/// <summary>
/// Группа людей workspace (docs/TZ_project_access.md §1, этап 4C): «Бэкенд», «Дизайн». Группе выдают роль в проекте
/// (<see cref="BoardGroup"/>) — и каждый её участник получает эту роль: итоговая роль — максимум из роли по умолчанию,
/// прямого участия и участия через группы. Состав — отдельные записи <see cref="GroupMember"/>: проверка прав
/// спрашивает «входит ли человек в группу проекта», и грузить ради этого группу целиком незачем.
/// </summary>
public sealed class Group
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 500;

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Команда (этап 4D): такую группу указывают в задаче (<see cref="TaskItem.TeamId"/>), по ней фильтруют (FQL <c>team</c>)
    /// и раскладывают дорожки канбана. Отдельной сущности «команда» нет — разница с группой в этом флаге.
    /// Снятый флаг задачи не трогает: команда в них остаётся, но новых задач ей уже не назначают.
    /// </summary>
    public bool IsTeam { get; private set; }

    private Group()
    {
        // EF Core
    }

    public static Group Create(string name, string? description, bool isTeam = false)
    {
        var group = new Group { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow, IsTeam = isTeam };
        group.Update(name, description);
        return group;
    }

    public void SetTeam(bool isTeam) => IsTeam = isTeam;

    /// <summary>Уникальность имени без учёта регистра проверяет Application (нужен список групп).</summary>
    public void Update(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Название группы не может быть пустым.", nameof(name));
        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Название группы — не длиннее {NameMaxLength} символов.", nameof(name));
        var text = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (text?.Length > DescriptionMaxLength)
            throw new ArgumentException($"Описание группы — не длиннее {DescriptionMaxLength} символов.", nameof(description));

        Name = trimmed;
        Description = text;
    }
}

/// <summary>Человек в группе. Деактивированный остаётся (история), права отсекает ActorResolver.</summary>
public sealed class GroupMember
{
    public Guid GroupId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTime AddedAt { get; private set; }

    private GroupMember()
    {
        // EF Core
    }

    public static GroupMember Create(Guid groupId, Guid userId)
    {
        if (groupId == Guid.Empty)
            throw new ArgumentException("Group id must not be empty.", nameof(groupId));
        if (userId == Guid.Empty)
            throw new ArgumentException("User id must not be empty.", nameof(userId));
        return new GroupMember { GroupId = groupId, UserId = userId, AddedAt = DateTime.UtcNow };
    }
}

/// <summary>Роль группы в проекте: все участники группы получают её (если она выше их прочих ролей в проекте).</summary>
public sealed class BoardGroup
{
    public Guid BoardId { get; private set; }

    public Guid GroupId { get; private set; }

    public ProjectRole Role { get; private set; }

    /// <summary>Свой набор прав (этап 4E); null — права роли. Role тогда — базовая роль набора.</summary>
    public Guid? PermissionSetId { get; private set; }

    public Guid AddedById { get; private set; }

    public DateTime AddedAt { get; private set; }

    private BoardGroup()
    {
        // EF Core
    }

    public static BoardGroup Create(Guid boardId, Guid groupId, ProjectRole role, Guid addedById, Guid? permissionSetId = null)
    {
        if (boardId == Guid.Empty)
            throw new ArgumentException("Board id must not be empty.", nameof(boardId));
        if (groupId == Guid.Empty)
            throw new ArgumentException("Group id must not be empty.", nameof(groupId));
        if (addedById == Guid.Empty)
            throw new ArgumentException("Added-by id must not be empty.", nameof(addedById));
        return new BoardGroup
        {
            BoardId = boardId, GroupId = groupId, Role = ValidateRole(role), PermissionSetId = permissionSetId, AddedById = addedById, AddedAt = DateTime.UtcNow
        };
    }

    /// <summary>true — роль или набор изменились; то же самое — no-op.</summary>
    public bool ChangeRole(ProjectRole role, Guid? permissionSetId = null)
    {
        role = ValidateRole(role);
        if (role == Role && permissionSetId == PermissionSetId)
            return false;
        Role = role;
        PermissionSetId = permissionSetId;
        return true;
    }

    private static ProjectRole ValidateRole(ProjectRole role) =>
        Enum.IsDefined(role) ? role : throw new ArgumentException($"Unknown project role {role}.", nameof(role));
}
