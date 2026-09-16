using MediatR;

namespace Flow.Application.Features.Attachments.Commands.AttachmentDeleteCommand;

/// <summary>false — вложения нет. Удалять может тот, кто приложил, либо Admin и Owner.</summary>
public sealed record AttachmentDeleteCommand(Guid ActorId, Guid AttachmentId) : IRequest<bool>;
