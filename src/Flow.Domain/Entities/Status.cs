namespace Flow.Domain.Entities;

/// <summary>
/// Статус задачи, настраиваемый на уровне доски (как колонка в канбане).
/// Создаётся и меняется только через методы <see cref="Board"/>, чтобы инварианты «ровно один начальный,
/// хотя бы один финальный, имя уникально в проекте» проверялись в одном месте (docs/TZ_workflow_config.md §1).
/// </summary>
public sealed class Status
{
    public Guid Id { get; private set; }

    public Guid BoardId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Порядковый номер колонки на доске (0, 1, 2...), не связан с бизнес-заказами.</summary>
    public int SortOrder { get; private set; }

    public bool IsInitial { get; private set; }

    public bool IsFinal { get; private set; }

    /// <summary>Из какого пресета DefaultStatuses создан статус; null — кастомный статус, добавленный вручную.</summary>
    public StatusType? Type { get; private set; }

    private Status()
    {
        // EF Core
    }

    internal Status(Guid boardId, string name, int sortOrder, bool isInitial, bool isFinal, StatusType? type = null)
    {
        Id = Guid.NewGuid();
        BoardId = boardId;
        Name = ValidateName(name);
        SortOrder = sortOrder;
        IsInitial = isInitial;
        IsFinal = isFinal;
        Type = type;
    }

    public const int NameMaxLength = 100;

    internal void Rename(string name) => Name = ValidateName(name);

    internal void SetInitial(bool isInitial) => IsInitial = isInitial;

    internal void SetFinal(bool isFinal) => IsFinal = isFinal;

    internal void SetType(StatusType? type) => Type = type;

    internal void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    internal static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Status name must not be empty.", nameof(name));

        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Status name must be at most {NameMaxLength} characters.", nameof(name));

        return trimmed;
    }
}
