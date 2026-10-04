using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserSetAvatarCommand;

/// <summary>
/// Загрузка своего аватара. UserId нет намеренно, как у настроек: менять картинку можно только себе,
/// любой роли, а чужую — никому, поэтому и строки в матрице прав нет.
/// Невалидный файл (пустой, больше <see cref="AvatarLimits.MaxBytes"/>, не PNG/JPEG/GIF/WebP) — ArgumentException → 400.
/// Обработчик - <see cref="UserSetAvatarCommandHandler"/>
/// </summary>
/// <param name="Content">Перематываемый поток: сначала читается сигнатура, потом он же уходит в хранилище.</param>
public sealed record UserSetAvatarCommand(
    Guid ActorId,
    string FileName,
    long SizeBytes,
    Stream Content) : IRequest<UserResponse>;
