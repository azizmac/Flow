using System.Text.Json;
using System.Text.Json.Serialization;
using Flow.Domain.Templates;

namespace Flow.Application.Features.Templates;

/// <summary>
/// Чертёж в JSON шаблона (<c>BoardTemplate.Payload</c>): перечисления строками — порядок enum'ов в коде может
/// поменяться, а сохранённый шаблон обязан читаться. Старые версии поднимаются апгрейдером до текущей перед разбором.
/// </summary>
public static class BoardBlueprintJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(BoardBlueprint blueprint) => JsonSerializer.Serialize(blueprint, Options);

    public static BoardBlueprint Deserialize(string payload, int version)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(payload)?.AsObject()
                   ?? throw new InvalidOperationException("Шаблон повреждён: пустой чертёж.");
        if (version > BoardBlueprint.CurrentVersion)
            throw new InvalidOperationException($"Шаблон сохранён более новой версией Flow (формат {version}).");

        // Апгрейдер: v1 → v2 → … по шагу за раз. Пока версия одна, шагов нет — место для них здесь.
        node["version"] = BoardBlueprint.CurrentVersion;

        var blueprint = node.Deserialize<BoardBlueprint>(Options) ?? throw new InvalidOperationException("Шаблон повреждён.");
        return blueprint with
        {
            Transitions = blueprint.Transitions ?? [],
            TaskTypes = blueprint.TaskTypes ?? [],
            CustomFields = blueprint.CustomFields ?? [],
            Screens = blueprint.Screens ?? [],
            SampleTasks = blueprint.SampleTasks ?? []
        };
    }
}
