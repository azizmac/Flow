using Flow.Application.Features.About.Queries.AboutQuery;
using Flow.Client.Services;
using Flow.Shared.Contracts.About;

namespace Flow.Api.Client;

/// <summary>«О системе»: срез AboutController. Actor не нужен — читать может любая роль.</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<AboutResponse>> GetAbout(CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new AboutQuery(), ct)));
}
