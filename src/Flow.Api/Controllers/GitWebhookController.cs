using Flow.Application.Features.GitIntegration;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Приём вебхуков Git-хостингов (docs/TZ_git_integration.md §2). Маршрут вне /api и анонимный: вебхук не несёт
/// Bearer, а под /api принимается только он. Доступ держит подпись по сырому телу; неверная — 401 без тела,
/// неизвестный или выключенный репозиторий — 404, повтор доставки — 200, принято — 202 (разбор — воркером,
/// хостинг ждёт ответ ~10 секунд). [ApiController] нет намеренно: иначе конвенция увела бы маршрут под /api.
/// </summary>
[AllowAnonymous]
public sealed class GitWebhookController(IMediator mediator, ILogger<GitWebhookController> logger) : ControllerBase
{
    public const long MaxBodyBytes = 5 * 1024 * 1024;

    [HttpPost("hooks/git/{repositoryId:guid}")]
    [RequestSizeLimit(MaxBodyBytes)]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Receive(Guid repositoryId, CancellationToken cancellationToken)
    {
        if (Request.ContentLength > MaxBodyBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var result = await mediator.Send(new GitWebhookReceiveCommand(repositoryId, headers, buffer.ToArray()), cancellationToken);
        if (result == GitWebhookResult.Unauthorized)
            logger.LogWarning("Вебхук для репозитория {Repository} с неверной подписью отклонён.", repositoryId);

        return result switch
        {
            GitWebhookResult.NotFound => NotFound(),
            GitWebhookResult.Unauthorized => Unauthorized(),
            GitWebhookResult.BadRequest => BadRequest(),
            GitWebhookResult.Duplicate => Ok(),
            _ => Accepted()
        };
    }
}
