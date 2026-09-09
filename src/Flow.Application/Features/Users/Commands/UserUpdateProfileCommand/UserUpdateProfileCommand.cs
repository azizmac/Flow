using MediatR;

namespace Flow.Application.Features.Users.Commands.UserUpdateProfileCommand;

/// <summary>
/// PATCH-семантика: null — поле не трогать. Чтобы очистить необязательное поле (JobTitle, Bio, PhoneNumber, AvatarUrl),
/// передаётся пустая строка — домен превращает её в null.
/// Username и Email меняются отдельными командами, потому что у них есть исход «занято» (409).
/// </summary>
public sealed record UserUpdateProfileCommand(
    Guid UserId,
    string? FirstName,
    string? LastName,
    string? JobTitle,
    string? Bio,
    string? PhoneNumber,
    string? AvatarUrl) : IRequest<UserUpdateResult>;
