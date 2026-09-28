namespace Flow.Domain.Entities;

/// <summary>
/// Шаблон проекта, сохранённый людьми (docs/TZ_workflow_config.md §4, этап 3F): имя, описание и чертёж конфигурации
/// в JSON (<see cref="Payload"/>, версия — <see cref="Version"/>). Разбор и апгрейд старых версий — Application:
/// домен хранит строку, чтобы сохранённый год назад шаблон читался кодом, который знает все версии. Встроенные
/// шаблоны в БД не лежат — они в коде (<see cref="Templates.BuiltInBoardTemplates"/>).
/// </summary>
public sealed class BoardTemplate
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 500;

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid CreatedById { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public int Version { get; private set; }

    public string Payload { get; private set; } = "{}";

    private BoardTemplate()
    {
        // EF Core
    }

    public static BoardTemplate Create(string name, string? description, Guid createdById, int version, string payload)
    {
        if (createdById == Guid.Empty)
            throw new ArgumentException("Creator id must not be empty.", nameof(createdById));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), version, "Template version must be positive.");
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        var template = new BoardTemplate { Id = Guid.NewGuid(), CreatedById = createdById, CreatedAt = DateTime.UtcNow, Version = version, Payload = payload };
        template.Rename(name, description);
        return template;
    }

    public void Rename(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Название шаблона не может быть пустым.", nameof(name));
        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Название шаблона — не длиннее {NameMaxLength} символов.", nameof(name));
        var text = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (text?.Length > DescriptionMaxLength)
            throw new ArgumentException($"Описание шаблона — не длиннее {DescriptionMaxLength} символов.", nameof(description));

        Name = trimmed;
        Description = text;
    }
}
