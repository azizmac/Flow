using Flow.Application.Features.Users.Queries.UserAvatarQuery;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Картинки аватаров для самих страниц: <c>&lt;img src="/avatars/{userId}/{имя}"&gt;</c>. Устроен как
/// FilesController и по тем же причинам: тег img заголовков не носит, а под /api принимается только
/// Bearer, поэтому маршрут вне префикса, без [ApiController] и с ведущим слэшем. Анонимно не отдаётся —
/// FallbackPolicy закрывает и его.
/// </summary>
public sealed class AvatarsController(IMediator mediator) : ControllerBase
{
    [HttpGet("/avatars/{userId:guid}/{fileName}")]
    public async Task<IActionResult> Get(Guid userId, string fileName, CancellationToken cancellationToken)
    {
        var avatar = await mediator.Send(new UserAvatarQuery(userId, fileName), cancellationToken);
        if (avatar is null)
            return NotFound();

        var headers = Response.Headers;

        // Те же рубежи, что у inline-вложений (AttachmentDelivery): тип определён сервером и сверен
        // по сигнатуре, но браузер угадывать его не должен, а открытый напрямую файл — ничего исполнять.
        headers.XContentTypeOptions = "nosniff";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers.ContentSecurityPolicy = "sandbox";

        // В отличие от вложений — кэш надолго: имя файла новое при каждой загрузке, и новая картинка
        // приходит по новому адресу. Аватар виден на каждой строке списка задач, и спрашивать 304
        // на каждую из них незачем.
        headers.CacheControl = "private, max-age=31536000, immutable";

        return File(avatar.Content, avatar.ContentType);
    }

    /// <summary>Мусор в адресе — 404, а не HTML-страница «не найдено» от фоллбэка Razor Components.</summary>
    [HttpGet("/avatars/{**rest}")]
    public IActionResult NotAnAvatar() => NotFound();
}
