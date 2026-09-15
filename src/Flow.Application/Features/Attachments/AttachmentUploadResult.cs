using Flow.Shared.Contracts.Attachments;

namespace Flow.Application.Features.Attachments;

/// <summary>
/// Исход загрузки. Отдельный результат, а не исключения: «файл слишком большой» и «такой уже приложен» —
/// это нормальные ответы (400 и 409), а не аварии.
/// </summary>
public sealed record AttachmentUploadResult(
    AttachmentUploadStatus Status,
    AttachmentResponse? Response = null,
    string? Error = null,
    Guid? DuplicateId = null)
{
    public bool IsNotFound => Status == AttachmentUploadStatus.TaskNotFound;

    public bool IsInvalid => Status == AttachmentUploadStatus.Invalid;

    public bool IsDuplicate => Status == AttachmentUploadStatus.Duplicate;

    public static AttachmentUploadResult TaskNotFound() => new(AttachmentUploadStatus.TaskNotFound);

    public static AttachmentUploadResult Invalid(string error) => new(AttachmentUploadStatus.Invalid, Error: error);

    public static AttachmentUploadResult Duplicate(Guid existingId) =>
        new(AttachmentUploadStatus.Duplicate, Error: "Такой файл уже приложен к этой задаче.", DuplicateId: existingId);

    public static AttachmentUploadResult Success(AttachmentResponse response) =>
        new(AttachmentUploadStatus.Success, response);
}

public enum AttachmentUploadStatus
{
    Success = 0,
    TaskNotFound = 1,
    Invalid = 2,
    Duplicate = 3
}
