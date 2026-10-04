using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardCreateCommand;

/// <summary>Result.IsKeyTaken = true, если доска с таким Key (после нормализации) уже существует.</summary>
/// <summary>ActorId — кто создаёт; право Admin+ (IPermissionService.EnsureCanManageBoards → 403).</summary>
/// <summary>
/// TemplateId — шаблон проекта (этап 3F): встроенный или сохранённый; null — «Простой», как раньше. Неизвестный — 400.
/// Обработчик - <see cref="BoardCreateCommandHandler"/>
/// </summary>
public sealed record BoardCreateCommand(Guid ActorId, string Name, string Key, Guid? TemplateId = null) : IRequest<BoardCreateResult>;
