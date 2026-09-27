using System.Text.Json;
using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Features.CustomFields;

/// <summary>
/// Запись значений пользовательских полей в задачу — один путь для создания и PATCH (docs/TZ_task_model.md §4):
/// поле проекта задачи, проверка значения доменом, «пользователь активен» — здесь (домен людей не видит),
/// журнал CustomFieldChanged по каждому изменённому полю. true — изменился текст (Text/LongText), и задаче
/// нужен Upsert в поиске: эти значения входят в чанк задачи.
/// </summary>
internal sealed class TaskCustomFields(IUserRepository users, ITaskActivityRepository activities)
{
    public async Task<bool> ApplyAsync(
        Board board, TaskItem task, IReadOnlyDictionary<Guid, JsonElement?> values, Guid actorId, bool journal, CancellationToken cancellationToken)
    {
        var textChanged = false;
        foreach (var (fieldId, value) in values)
        {
            var field = board.CustomFields.FirstOrDefault(f => f.Id == fieldId)
                ?? throw new InvalidOperationException($"Custom field {fieldId} is not found in project {board.Key}.");

            if (field.Type == CustomFieldType.User && value is { ValueKind: JsonValueKind.String } userValue)
            {
                var user = Guid.TryParse(userValue.GetString(), out var userId) ? await users.GetByIdAsync(userId, cancellationToken) : null;
                if (user is null || !user.IsActive)
                    throw new InvalidOperationException($"Поле «{field.Name}»: пользователь не найден или деактивирован.");
            }

            if (task.SetCustomField(field, value) is not { } change)
                continue;

            textChanged |= field.Type is CustomFieldType.Text or CustomFieldType.LongText;
            if (journal)
                activities.Add(TaskActivity.CustomFieldChanged(task.Id, actorId, field.Id, change.Old, change.New));
        }

        return textChanged;
    }

    /// <summary>Обязательные поля типа задачи должны быть заполнены — при создании и смене типа (400 с именами полей).</summary>
    public static void EnsureRequired(Board board, TaskItem task, Guid typeId)
    {
        var missing = board.MissingRequiredFields(task, typeId);
        if (missing.Count > 0)
            throw new InvalidOperationException($"Заполните обязательные поля: {string.Join(", ", missing.Select(f => $"«{f.Name}»"))}.");
    }
}
