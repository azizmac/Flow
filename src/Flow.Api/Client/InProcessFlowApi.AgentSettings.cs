using Flow.Application.Features.Agents.Settings;
using Flow.Client.Services;
using Flow.Shared.Contracts.Agents;

namespace Flow.Api.Client;

/// <summary>Провайдеры и ключи агента — коды как у AgentSettingsController; ошибки ввода ловит Guard (400).</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<AgentStatusResponse>> GetAgentStatus(CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new AgentStatusQuery(await ActorAsync()), ct)));

    public Task<ApiResult<IReadOnlyList<AgentCatalogProviderResponse>>> GetAgentCatalog(CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new AgentCatalogQuery(await ActorAsync()), ct)));

    public Task<ApiResult<IReadOnlyList<AgentProviderResponse>>> GetAgentProviders(CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new AgentProviderListQuery(await ActorAsync()), ct)));

    public Task<ApiResult<AgentProviderResponse>> CreateAgentProvider(CreateAgentProviderRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new AgentProviderCreateCommand(await ActorAsync(), request.ProviderKey, request.IsCustom,
            request.Name, request.BaseUrl, request.Models, request.ApiKey), ct)));

    public Task<ApiResult<AgentProviderResponse>> UpdateAgentProvider(Guid id, UpdateAgentProviderRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new AgentProviderUpdateCommand(await ActorAsync(), id, request.Name, request.BaseUrl,
            request.Models, request.ApiKey, request.ClearApiKey), ct) is { } provider
            ? Ok(provider) : NotFound<AgentProviderResponse>());

    public Task<ApiResult<bool>> DeleteAgentProvider(Guid id, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new AgentProviderDeleteCommand(await ActorAsync(), id), ct) ? Ok(true) : NotFound<bool>());

    public Task<ApiResult<AgentStatusResponse>> SetAgentDefaultModel(SetAgentDefaultModelRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new AgentDefaultModelSetCommand(await ActorAsync(), request.ProviderKey, request.ModelId), ct)));

    public Task<ApiResult<AgentCheckResponse>> CheckAgent(AgentCheckRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new AgentCheckCommand(await ActorAsync(), request.ProviderKey, request.ModelId), ct)));

    public Task<ApiResult<IReadOnlyList<AgentProviderResponse>>> ApplyAgentConfig(CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new AgentApplyCommand(await ActorAsync()), ct)));
}
