using MediatR;

namespace Flow.Application.Features.Attachments.Queries.AttachmentContentQuery;

/// <summary>
/// Содержимое вложения для скачивания. null — вложения нет или объект пропал из хранилища.
/// Публичных ссылок нет: файл отдаётся только по авторизованному запросу (docs/TZ_attachments.md).
/// </summary>
public sealed record AttachmentContentQuery(Guid AttachmentId) : IRequest<AttachmentContent?>;

/// <param name="CanInline">
/// Можно ли показывать файл в браузере. Решает сервер: тип уже проверен по сигнатуре при загрузке,
/// а inline для произвольного типа — это XSS на своём origin.
/// </param>
public sealed record AttachmentContent(Stream Content, string FileName, string ContentType, long SizeBytes, bool CanInline);
