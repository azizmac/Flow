using Flow.Application.Features.About.Queries.AboutQuery;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>Версия, модули и лимиты — экран «Настройки → О системе». Любая роль, но только с сессией (FallbackPolicy).</summary>
[ApiController]
[Route("about")]
public class AboutController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new AboutQuery(), cancellationToken));
}
