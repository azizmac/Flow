using MediatR;

namespace Flow.Application.Features.Users.Queries.UserAvatarQuery;

/// <summary>
/// Картинка аватара по адресу <c>/avatars/{UserId}/{FileName}</c>. null — нет такого человека,
/// файл уже заменён другим или объект пропал из хранилища. Читать может любая роль, как и сам профиль.
/// </summary>
public sealed record UserAvatarQuery(Guid UserId, string FileName) : IRequest<UserAvatarContent?>;

public sealed record UserAvatarContent(Stream Content, string ContentType);
