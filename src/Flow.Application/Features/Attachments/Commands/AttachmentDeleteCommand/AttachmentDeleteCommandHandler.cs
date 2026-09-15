using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Attachments.Commands.AttachmentDeleteCommand;

/// <summary>
/// Обратный загрузке порядок: сначала транзакция, потом объект. Пока строка не удалена, файл должен
/// оставаться скачиваемым; осиротевший объект после сбоя — меньшее зло, чем строка без файла.
/// </summary>
internal sealed class AttachmentDeleteCommandHandler(
    IAttachmentRepository attachments,
    ITaskActivityRepository activities,
    IFileStorage storage,
    ActorResolver actors,
    IPermissionService permissions,
    IUnitOfWork unitOfWork)
    : IRequestHandler<AttachmentDeleteCommand, bool>
{
    public async Task<bool> Handle(AttachmentDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var attachment = await attachments.GetByIdAsync(request.AttachmentId, cancellationToken);
        if (attachment is null)
            return false;

        permissions.EnsureCanDeleteAttachment(actor, attachment);

        attachments.Remove(attachment);
        activities.Add(TaskActivity.AttachmentRemoved(attachment.TaskId, actor.Id, attachment.Id, attachment.FileName));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await storage.DeleteAsync(attachment.StorageKey, cancellationToken);
        }
        catch
        {
            // Хранилище недоступно: строки уже нет, объект останется мусором. Ронять запрос из-за этого
            // нельзя — для пользователя вложение удалено.
        }

        return true;
    }
}
