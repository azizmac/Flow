using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Flow.Shared.Ids;

/// <summary>
/// Идентификатор доски. В БД хранится как uuid (EF value converter), наружу сериализуется как "boa_...".
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<BoardId>))]
public sealed record BoardId(Guid Value) : ITypedId<BoardId>
{
    public static string Prefix => "boa";

    public static BoardId New() => new(Guid.NewGuid());

    public static BoardId Create(Guid value) => new(value);

    public static bool TryParse(string? raw, [NotNullWhen(true)] out BoardId? id) => TypedIdFormat.TryParse(raw, Prefix, Create, out id);

    public static BoardId Parse(string raw) => TypedIdFormat.Parse(raw, Prefix, Create);

    public override string ToString() => TypedIdFormat.Format(Prefix, Value);
}
