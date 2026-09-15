using MediatR;

namespace Flow.Application.Features.Attachments.Commands.AttachmentUploadCommand;

/// <summary>
/// Загрузка файла к задаче. ActorId — Member и выше (403 для Reader).
/// </summary>
/// <param name="Content">
/// Поток файла. Обязан быть перематываемым: сначала по нему считается SHA-256 и сигнатура,
/// потом он же уходит в хранилище. ASP.NET отдаёт именно такой (IFormFile.OpenReadStream).
/// </param>
/// <param name="SizeBytes">Размер, известный до чтения: лимит проверяется раньше, чем файл прочитан.</param>
public sealed record AttachmentUploadCommand(
    Guid ActorId,
    Guid TaskId,
    string FileName,
    long SizeBytes,
    Stream Content) : IRequest<AttachmentUploadResult>;
