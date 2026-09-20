using MediatR;

namespace Flow.Application.Features.Attachments.Queries.AttachmentMetaQuery;

/// <summary>
/// Карточка вложения без содержимого. Нужна прямым ссылкам /files/{id}: по ней строится ETag и
/// отдаётся 304, не открывая поток в S3. Читать строку ради этого обязательно — именно она
/// отвечает на вопрос «файл ещё существует», а без неё удаление вложения переставало бы
/// действовать (браузер получал бы 304 на давно удалённый файл).
/// </summary>
public sealed record AttachmentMetaQuery(Guid AttachmentId) : IRequest<AttachmentMeta?>;

/// <param name="ContentHash">SHA-256 содержимого, уже посчитанный при загрузке — из него делается ETag.</param>
/// <param name="CanInline">
/// Можно ли показывать файл в браузере. Решает сервер: тип проверен по сигнатуре при загрузке,
/// а inline для произвольного типа — это XSS на своём origin.
/// </param>
public sealed record AttachmentMeta(
    string FileName,
    string ContentType,
    long SizeBytes,
    byte[] ContentHash,
    bool CanInline);
