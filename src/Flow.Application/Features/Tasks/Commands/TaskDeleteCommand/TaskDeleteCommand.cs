using Flow.Shared.Ids;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;

/// <summary>true — удалено, false — задача не найдена.</summary>
public sealed record TaskDeleteCommand(TaskId TaskId) : IRequest<bool>;
