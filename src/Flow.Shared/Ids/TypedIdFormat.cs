using System.Diagnostics.CodeAnalysis;

namespace Flow.Shared.Ids;

/// <summary>
/// Общие формат и парсинг строго типизированных id: "префикс_guid(N)",
/// где guid(N) — 32 hex-символа без дефисов (например "boa_9d0f1c2e4b7a4d3e8f1a2b3c4d5e6f70").
/// </summary>
public static class TypedIdFormat
{
    public const char Separator = '_';

    public static string Format(string prefix, Guid value) => $"{prefix}{Separator}{value:N}";

    public static bool TryParse<T>(string? raw, string prefix, Func<Guid, T> create, [NotNullWhen(true)] out T? id)
        where T : class
    {
        id = null;

        if (raw is null || !raw.StartsWith($"{prefix}{Separator}", StringComparison.Ordinal))
            return false;

        if (!Guid.TryParseExact(raw.AsSpan()[(prefix.Length + 1)..], "N", out var value))
            return false;

        id = create(value);
        return true;
    }

    public static T Parse<T>(string raw, string prefix, Func<Guid, T> create)
        where T : class =>
        TryParse(raw, prefix, create, out var id)
            ? id
            : throw new ArgumentException(
                $"Invalid id '{raw}', expected format '{prefix}{Separator}<guid>'.",
                nameof(raw));
}
