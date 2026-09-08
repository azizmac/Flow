using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Flow.Shared.Ids;

/// <summary>
/// Идентификатор статуса задачи. В БД хранится как uuid (EF value converter), наружу сериализуется как "sta_...".
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<StatusId>))]
public sealed record StatusId(Guid Value) : ITypedId<StatusId>
{
    public static string Prefix => "sta";

    public static StatusId New() => new(Guid.NewGuid());

    public static StatusId Create(Guid value) => new(value);

    public static bool TryParse(string? raw, [NotNullWhen(true)] out StatusId? id) => TypedIdFormat.TryParse(raw, Prefix, Create, out id);

    public static StatusId Parse(string raw) => TypedIdFormat.Parse(raw, Prefix, Create);

    public override string ToString() => TypedIdFormat.Format(Prefix, Value);
}
