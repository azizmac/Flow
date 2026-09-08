using System.Diagnostics.CodeAnalysis;

namespace Flow.Shared.Ids;

/// <summary>
/// Контракт строго типизированного идентификатора сущности (Strongly Typed Id, формат Stripe):
/// record, оборачивающий Guid, чей ToString() возвращает "префикс_guid(N)" (например "boa_9d0f...c21").
/// Static-abstract члены позволяют generic-конвертеру <see cref="TypedIdJsonConverter{T}"/>
/// парсить любой id, не зная конкретного типа.
/// </summary>
public interface ITypedId<TSelf> where TSelf : ITypedId<TSelf>
{
    Guid Value { get; }

    /// <summary>Короткий префикс сущности (первые три буквы): "boa", "tas", "sta".</summary>
    static abstract string Prefix { get; }

    /// <summary>Разбор строки формата "префикс_guid(N)"; false — если префикс чужой или Guid невалиден.</summary>
    static abstract bool TryParse(string? raw, [NotNullWhen(true)] out TSelf? id);
}
