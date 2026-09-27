using System.Text.Json;
using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>Пользовательские поля (docs/TZ_task_model.md §4): определения в проекте, проверка и хранение значений.</summary>
public class CustomFieldTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Key_Is_Validated_And_Unique_Options_Only_For_Lists()
    {
        var board = Board.Create("Проект", "PRJ");
        board.AddCustomField("sla_level", "SLA", CustomFieldType.Select, ["Gold", "Silver"]);

        Assert.Throws<ArgumentException>(() => board.AddCustomField("SLA", "Другое", CustomFieldType.Text));
        Assert.Throws<InvalidOperationException>(() => board.AddCustomField("sla_level", "Другое", CustomFieldType.Text));
        Assert.Throws<InvalidOperationException>(() => board.AddCustomField("other", "sla", CustomFieldType.Text));
        Assert.Throws<InvalidOperationException>(() => board.AddCustomField("empty", "Пусто", CustomFieldType.Select, []));
        Assert.Throws<InvalidOperationException>(() => board.AddCustomField("note", "Заметка", CustomFieldType.Text, ["лишнее"]));
        Assert.Throws<InvalidOperationException>(() => board.AddCustomField("dup", "Дубли", CustomFieldType.MultiSelect, ["A", "a"]));
    }

    [Theory]
    [InlineData(CustomFieldType.Text, "\"  привет \"", "\"привет\"")]
    [InlineData(CustomFieldType.Text, "\"   \"", null)]
    [InlineData(CustomFieldType.Number, "12.5", "12.5")]
    [InlineData(CustomFieldType.Date, "\"2026-10-01\"", "\"2026-10-01\"")]
    [InlineData(CustomFieldType.Checkbox, "false", "false")]
    [InlineData(CustomFieldType.Url, "\"https://flow.dev/a\"", "\"https://flow.dev/a\"")]
    public void Values_Are_Normalized(CustomFieldType type, string input, string? stored)
    {
        var board = Board.Create("Проект", "PRJ");
        var field = board.AddCustomField("field", "Поле", type);

        Assert.Equal(stored, CustomFieldValidator.Validate(field, Json(input))?.ToJsonString(CustomFieldValidator.JsonOptions));
    }

    [Theory]
    [InlineData(CustomFieldType.Number, "\"12\"")]
    [InlineData(CustomFieldType.Date, "\"01.10.2026\"")]
    [InlineData(CustomFieldType.Url, "\"javascript:alert(1)\"")]
    [InlineData(CustomFieldType.Checkbox, "\"да\"")]
    [InlineData(CustomFieldType.User, "\"ivan\"")]
    public void Wrong_Values_Are_Rejected(CustomFieldType type, string input)
    {
        var field = Board.Create("Проект", "PRJ").AddCustomField("field", "Поле", type);

        Assert.Throws<ArgumentException>(() => CustomFieldValidator.Validate(field, Json(input)));
    }

    [Fact]
    public void Task_Stores_Options_By_Id_Checks_Type_And_Required()
    {
        var board = Board.Create("Проект", "PRJ");
        var bug = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Bug);
        var tags = board.AddCustomField("tags", "Теги", CustomFieldType.MultiSelect, ["UI", "API"]);
        var severity = board.AddCustomField("severity", "Серьёзность", CustomFieldType.Text, isRequired: true, taskTypeIds: [bug.Id]);
        var task = board.CreateTask("Задача");
        var ui = tags.Options[0].Id;

        var change = task.SetCustomField(tags, Json($"[\"{ui}\", \"{ui}\"]"));

        Assert.Equal((null, $"[\"{ui}\"]"), change);
        Assert.Null(task.SetCustomField(tags, Json($"[\"{ui}\"]")));
        Assert.Throws<ArgumentException>(() => task.SetCustomField(tags, Json($"[\"{Guid.NewGuid()}\"]")));
        Assert.Throws<InvalidOperationException>(() => task.SetCustomField(severity, Json("\"high\"")));
        Assert.Equal([severity], board.MissingRequiredFields(task, bug.Id));
        Assert.Empty(board.MissingRequiredFields(task, task.TypeId));

        // Удалённый вариант задачу не трогает: значение остаётся до следующей правки.
        board.UpdateCustomField(tags.Id, options: [(tags.Options[1].Id, "API", null)]);
        Assert.Equal($"[\"{ui}\"]", task.GetCustomField(tags.Id)!.Value.GetRawText());
        task.SetCustomField(tags, null);
        Assert.Empty(task.CustomFieldValues());
    }
}
