using MediatR;

namespace Flow.Application.Features.Users.Commands.UserUpdateProfileCommand;

/// <summary>
/// PATCH-семантика: null — поле не трогать. Чтобы очистить необязательное поле (JobTitle, Bio, PhoneNumber),
/// передаётся пустая строка — домен превращает её в null.
/// Username и Email меняются отдельными командами, потому что у них есть исход «занято» (409).
/// Аватар — тоже отдельно (UserSetAvatarCommand): его загружают картинкой, и только себе.
/// Обработчик - <see cref="UserUpdateProfileCommandHandler"/>
/// </summary>
public sealed record UserUpdateProfileCommand(
    Guid ActorId,
    Guid UserId,
    string? FirstName,
    string? LastName,
    string? JobTitle,
    string? Bio,
    string? PhoneNumber) : IRequest<UserUpdateResult>;
