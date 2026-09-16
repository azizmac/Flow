namespace Flow.Application.Features.Attachments;

/// <summary>
/// Секция "Attachments" (docs/TZ_attachments.md). Биндится в Flow.Infrastructure и регистрируется
/// синглтоном — как SearchOptions: тянуть Microsoft.Extensions.Options в Application ради двух секций незачем.
/// </summary>
public sealed class AttachmentOptions
{
    public const string SectionName = "Attachments";

    /// <summary>25 МБ. Видео и тяжёлые макеты сюда не помещаются намеренно — им место в файловом хранилище.</summary>
    public long MaxFileBytes { get; set; } = 25L * 1024 * 1024;

    public int MaxPerTask { get; set; } = 20;

    public long MaxTotalBytesPerTask { get; set; } = 100L * 1024 * 1024;

    /// <summary>
    /// Список запрещающий, а не разрешающий: в трекер носят что угодно (.dwg, .psd, архивы),
    /// и белый список мешал бы каждый день, закрывая ровно те же случаи.
    /// </summary>
    public string[] BlockedExtensions { get; set; } =
        [".exe", ".dll", ".msi", ".bat", ".cmd", ".com", ".scr", ".ps1", ".sh", ".vbs", ".jar"];

    /// <summary>
    /// Что разрешено показывать прямо в интерфейсе. SVG сюда не входит: это документ со скриптами,
    /// а отдаётся он с нашего origin — то есть получил бы доступ к сессии.
    /// </summary>
    public string[] InlineContentTypes { get; set; } =
        ["image/png", "image/jpeg", "image/gif", "image/webp"];
}
