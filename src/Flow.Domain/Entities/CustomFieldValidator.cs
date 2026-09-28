using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flow.Domain.Entities;

/// <summary>
/// Проверка значения пользовательского поля (docs/TZ_task_model.md §4) — чистая функция: тип, длина, существование
/// варианта, схема ссылки. Возвращает нормализованное значение для хранения или null — «очистить» (пустая строка,
/// пустой список). Что пользователь существует и активен, проверяет хендлер: домен людей не видит.
/// Форма хранения: строка (Text, LongText, Url, Date «yyyy-MM-dd», Select и User — Id строкой), число (Number),
/// массив строк-Id (MultiSelect), true/false (Checkbox).
/// </summary>
public static class CustomFieldValidator
{
    public const int TextMaxLength = 500;
    public const int LongTextMaxLength = 10000;
    public const int UrlMaxLength = 2000;

    /// <summary>Запись значений без \uXXXX для кириллицы: JSON уходит в журнал задачи, его читают люди.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static JsonNode? Validate(CustomFieldDefinition field, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        return field.Type switch
        {
            CustomFieldType.Text => Text(field, value, TextMaxLength),
            CustomFieldType.LongText => Text(field, value, LongTextMaxLength),
            CustomFieldType.Number => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
                ? JsonValue.Create(number)
                : throw Invalid(field, "ожидается число"),
            CustomFieldType.Date => Text(field, value, 10) is { } date
                ? DateOnly.TryParseExact(date.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? JsonValue.Create(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    : throw Invalid(field, "ожидается дата yyyy-MM-dd")
                : null,
            CustomFieldType.Select => Text(field, value, 36) is { } option ? JsonValue.Create(Option(field, option.GetValue<string>()).ToString()) : null,
            CustomFieldType.MultiSelect => Options(field, value),
            CustomFieldType.User => Text(field, value, 36) is { } user
                ? Guid.TryParse(user.GetValue<string>(), out var userId) ? JsonValue.Create(userId.ToString()) : throw Invalid(field, "ожидается Id пользователя")
                : null,
            CustomFieldType.Checkbox => value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? JsonValue.Create(value.GetBoolean())
                : throw Invalid(field, "ожидается true или false"),
            CustomFieldType.Url => Text(field, value, UrlMaxLength) is { } url
                ? Uri.TryCreate(url.GetValue<string>(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                    ? url
                    : throw Invalid(field, "ссылка должна начинаться с http:// или https://")
                : null,
            _ => throw Invalid(field, "неизвестный тип")
        };
    }

    private static JsonValue? Text(CustomFieldDefinition field, JsonElement value, int maxLength)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw Invalid(field, "ожидается строка");

        var text = value.GetString()!.Trim();
        if (text.Length > maxLength)
            throw Invalid(field, $"не длиннее {maxLength} символов");
        return text.Length == 0 ? null : JsonValue.Create(text);
    }

    private static Guid Option(CustomFieldDefinition field, string raw) =>
        Guid.TryParse(raw, out var id) && field.FindOption(id) is not null ? id : throw Invalid(field, "такого варианта нет");

    private static JsonArray? Options(CustomFieldDefinition field, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw Invalid(field, "ожидается список вариантов");

        var ids = value.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? Option(field, e.GetString()!) : throw Invalid(field, "ожидается список вариантов"))
            .Distinct()
            .ToList();
        return ids.Count == 0 ? null : new JsonArray(ids.Select(id => (JsonNode?)JsonValue.Create(id.ToString())).ToArray());
    }

    private static ArgumentException Invalid(CustomFieldDefinition field, string what) => new($"Поле «{field.Name}»: {what}.");
}
