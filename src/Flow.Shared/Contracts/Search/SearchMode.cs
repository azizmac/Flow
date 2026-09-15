namespace Flow.Shared.Contracts.Search;

/// <summary>
/// Какими половинами гибрида искать. Hybrid — обычный режим; остальные нужны для отладки качества
/// и для деградации, когда модель эмбеддингов недоступна.
/// </summary>
public enum SearchMode
{
    /// <summary>Вектор + полнотекст, слияние RRF.</summary>
    Hybrid = 0,

    /// <summary>Только вектор: «по смыслу», без точных совпадений.</summary>
    Semantic = 1,

    /// <summary>Только полнотекст: работает без модели, поэтому в него же уходит деградация.</summary>
    Text = 2
}
