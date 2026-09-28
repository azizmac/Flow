namespace Flow.Client.Components;

/// <summary>Вариант <see cref="DsSegmented{T}"/>: значение и подпись.</summary>
public sealed record DsSegment<T>(T Value, string Label);
