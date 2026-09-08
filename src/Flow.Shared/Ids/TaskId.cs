using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Flow.Shared.Ids;

/// <summary>
/// Идентификатор задачи (TaskItem). В БД хранится как uuid (EF value converter), наружу сериализуется как "tas_...".
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<TaskId>))]
public sealed record TaskId(Guid Value) : ITypedId<TaskId>
{
    public static string Prefix => "tas";

    public static TaskId New() => new(Guid.NewGuid());

    public static TaskId Create(Guid value) => new(value);

    public static bool TryParse(string? raw, [NotNullWhen(true)] out TaskId? id) => TypedIdFormat.TryParse(raw, Prefix, Create, out id);

    public static TaskId Parse(string raw) => TypedIdFormat.Parse(raw, Prefix, Create);

    public override string ToString() => TypedIdFormat.Format(Prefix, Value);
}
