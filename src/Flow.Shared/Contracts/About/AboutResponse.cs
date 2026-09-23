namespace Flow.Shared.Contracts.About;

/// <summary>
/// «О системе» — GET /about, любая роль. Только то, что безопасно показать каждому:
/// версия, включённые модули и лимиты, с которыми человек сталкивается сам (размер вложения).
/// Адреса сервисов, строки подключения и имена моделей сюда не попадают намеренно.
/// </summary>
/// <param name="Version">Версия сборки хоста без хвоста «+commit».</param>
/// <param name="Commit">Короткий хеш коммита, если SDK вписал его в InformationalVersion; иначе null.</param>
/// <param name="Runtime">Например «.NET 10.0.0».</param>
/// <param name="StartedAt">Когда запущен процесс (UTC).</param>
public sealed record AboutResponse(
    string Version,
    string? Commit,
    string Runtime,
    DateTime StartedAt,
    AboutModules Modules,
    AboutLimits Limits);

/// <param name="Search">Search:Enabled — поиск и индексация вообще.</param>
/// <param name="SmartSearch">Search:Embeddings:Enabled — поиск по смыслу; false — только по словам.</param>
/// <param name="ImageSearch">Поиск по содержимому картинок и сканов.</param>
/// <param name="Rerank">Кнопка «Точнее» на странице поиска.</param>
public sealed record AboutModules(bool Search, bool SmartSearch, bool ImageSearch, bool Rerank);

public sealed record AboutLimits(long MaxFileBytes, int MaxFilesPerTask, long MaxTotalBytesPerTask);
