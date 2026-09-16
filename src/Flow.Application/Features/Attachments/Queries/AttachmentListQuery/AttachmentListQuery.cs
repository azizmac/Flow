using Flow.Shared.Contracts.Attachments;
using MediatR;

namespace Flow.Application.Features.Attachments.Queries.AttachmentListQuery;

/// <summary>Вложения задачи по возрастанию UploadedAt. null — задачи нет. Читать может любая роль.</summary>
public sealed record AttachmentListQuery(Guid TaskId) : IRequest<IReadOnlyList<AttachmentResponse>?>;
