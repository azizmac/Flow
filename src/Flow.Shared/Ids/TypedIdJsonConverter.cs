using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flow.Shared.Ids;

/// <summary>
/// Сериализует строго типизированный id как строку "префикс_guid" и парсит обратно.
/// Подключается атрибутом [JsonConverter] на каждом id (см. <see cref="BoardId"/>), поэтому работает
/// и в ASP.NET Core, и в Blazor без дополнительной настройки JsonSerializerOptions.
/// Null обрабатывает сам System.Text.Json (HandleNull для ссылочных типов по умолчанию false).
/// </summary>
public sealed class TypedIdJsonConverter<T> : JsonConverter<T>
    where T : class, ITypedId<T>
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var raw = reader.GetString();
        if (raw is not null && T.TryParse(raw, out var id))
            return id;

        throw new JsonException($"Invalid {typeof(T).Name} '{raw}', expected format '{T.Prefix}{TypedIdFormat.Separator}<guid>'.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
