using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskCreateCommand;

/// <summary>Бросает ArgumentException/InvalidOperationException при невалидных данных (см. Board.CreateTask).</summary>
internal sealed class TaskCreateCommandHandler(IBoardRepository boards, ITaskItemRepository tasks, IUnitOfWork unitOfWork)
    : IRequestHandler<TaskCreateCommand, TaskResponse?>
{
    public async Task<TaskResponse?> Handle(TaskCreateCommand request, CancellationToken cancellationToken)
    {
        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        var task = board.CreateTask(request.Title, request.Description, request.StatusId);

        // Board.Tasks не подгружен (не нужен для создания), поэтому EF не отследит новую задачу
        // через изменение коллекции сам — регистрируем её явно.
        tasks.Add(task);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return task.ToResponse();
    }
}
