namespace Flow.Domain.Entities;

/// <summary>
/// Человекочитаемый идентификатор задачи вида "FLW-42" (ключ доски + порядковый номер).
/// Генерируется один раз при создании задачи через <see cref="Board.CreateTask"/> и не меняется.
/// </summary>
public sealed record TaskCode
{
    public string Value { get; }

    private TaskCode(string value)
    {
        Value = value;
    }

    public static TaskCode Create(string boardKey, int number)
    {
        if (string.IsNullOrWhiteSpace(boardKey))
            throw new ArgumentException("Board key must not be empty.", nameof(boardKey));

        if (number < 1)
            throw new ArgumentOutOfRangeException(nameof(number), number, "Task number must be positive.");

        return new TaskCode($"{boardKey}-{number}");
    }

    /// <summary>Восстановление значения из уже сохранённых данных (EF Core value converter).</summary>
    public static TaskCode FromValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Task code value must not be empty.", nameof(value));

        return new TaskCode(value);
    }

    public override string ToString() => Value;
}
