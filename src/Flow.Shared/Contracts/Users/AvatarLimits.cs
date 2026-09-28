namespace Flow.Shared.Contracts.Users;

/// <summary>
/// Правила загрузки аватара — общие для сервера (проверка) и интерфейса (accept у поля выбора файла,
/// ранний отказ до передачи байтов по circuit'у).
/// </summary>
public static class AvatarLimits
{
    /// <summary>5 МБ: фото с телефона влезает, а плитка 32×32 в сайдбаре не тянет за собой 20-мегабайтный снимок.</summary>
    public const long MaxBytes = 5L * 1024 * 1024;

    /// <summary>Значение атрибута accept: только растровые форматы, которые сервер умеет сверить по сигнатуре.</summary>
    public const string Accept = "image/png,image/jpeg,image/gif,image/webp";
}
