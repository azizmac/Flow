namespace Flow.Domain.Entities;

/// <summary>
/// Прямое участие человека в проекте с ролью (docs/TZ_project_access.md §1). Отдельная сущность, а не коллекция
/// <see cref="Board"/>: права считаются на каждый запрос по паре (проект, человек), и тянуть ради этого всех
/// участников вместе с доской незачем. Роль участия только повышает: итоговая роль — максимум из роли
/// по умолчанию и участия (ProjectRoles.Effective).
/// </summary>
public sealed class BoardMember
{
    public Guid BoardId { get; private set; }

    public Guid UserId { get; private set; }

    public ProjectRole Role { get; private set; }

    /// <summary>Кто добавил (User.Id).</summary>
    public Guid AddedById { get; private set; }

    public DateTime AddedAt { get; private set; }

    private BoardMember()
    {
        // EF Core
    }

    private BoardMember(Guid boardId, Guid userId, ProjectRole role, Guid addedById)
    {
        BoardId = boardId;
        UserId = userId;
        Role = role;
        AddedById = addedById;
        AddedAt = DateTime.UtcNow;
    }

    public static BoardMember Create(Guid boardId, Guid userId, ProjectRole role, Guid addedById)
    {
        if (boardId == Guid.Empty)
            throw new ArgumentException("Board id must not be empty.", nameof(boardId));
        if (userId == Guid.Empty)
            throw new ArgumentException("User id must not be empty.", nameof(userId));
        if (addedById == Guid.Empty)
            throw new ArgumentException("Added-by id must not be empty.", nameof(addedById));

        return new BoardMember(boardId, userId, ValidateRole(role), addedById);
    }

    /// <summary>true — роль изменилась; та же роль — no-op.</summary>
    public bool ChangeRole(ProjectRole role)
    {
        role = ValidateRole(role);
        if (role == Role)
            return false;

        Role = role;
        return true;
    }

    private static ProjectRole ValidateRole(ProjectRole role) =>
        Enum.IsDefined(role) ? role : throw new ArgumentException($"Unknown project role {role}.", nameof(role));
}
