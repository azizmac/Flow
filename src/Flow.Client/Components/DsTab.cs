namespace Flow.Client.Components;

/// <summary>Вкладка <see cref="DsTabs"/>: ключ, подпись и необязательный счётчик рядом с подписью.</summary>
public sealed record DsTab(string Key, string Label, int? Count = null);
