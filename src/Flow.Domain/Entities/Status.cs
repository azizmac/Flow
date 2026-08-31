namespace Flow.Domain.Entities;

/// <summary>
/// Статус задачи, настраиваемый на уровне доски (как колонка в канбане).
/// Создаётся только через <see cref="Board.AddStatus"/>, чтобы инвариант
/// "не более одного начального и одного финального статуса на доску" проверялся в одном месте.
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

    public void Rename(string name) => Name = ValidateName(name);

    internal void SetInitial(bool isInitial) => IsInitial = isInitial;

    internal void SetFinal(bool isFinal) => IsFinal = isFinal;

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Status name must not be empty.", nameof(name));

        return name.Trim();
    }
}
