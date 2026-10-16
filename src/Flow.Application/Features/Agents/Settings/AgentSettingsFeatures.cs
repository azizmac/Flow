using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Domain.Entities.Agents;
using Flow.Shared.Contracts.Agents;
using MediatR;

namespace Flow.Application.Features.Agents.Settings;

// Инфраструктура агента: провайдеры моделей, ключи и модель по умолчанию для OpenCode («Настройки → Агент»).
// Права — глобальные Admin+ (EnsureCanManageAgent). Источник истины — Flow: ключ хранится зашифрованным
// (IAgentSecretProtector), OpenCode получает его через AgentConfigSync — сразу после правки и заново, если контейнер
// пересоздали. Логику функций агента (вопросы по коду, разбор требований) эти команды не трогают.

public sealed record AgentStatusQuery(Guid ActorId) : IRequest<AgentStatusResponse>;

public sealed record AgentCatalogQuery(Guid ActorId) : IRequest<IReadOnlyList<AgentCatalogProviderResponse>>;

public sealed record AgentProviderListQuery(Guid ActorId) : IRequest<IReadOnlyList<AgentProviderResponse>>;

/// <summary>
/// Подключить провайдера: из каталога OpenCode (ProviderKey — его Id) или кастомного (свой Id, адрес, модели).
/// Ключ необязателен (LM Studio, локальный сервер). После сохранения конфигурация сразу уходит в OpenCode; не дошла —
/// причина в ApplyError, повторит синхронизация.
/// </summary>
public sealed record AgentProviderCreateCommand(Guid ActorId, string ProviderKey, bool IsCustom, string? Name, string? BaseUrl,
    IReadOnlyList<AgentModelInfo>? Models, string? ApiKey) : IRequest<AgentProviderResponse>;

/// <summary>PATCH: null — не менять; ApiKey — заменить ключ; ClearApiKey — удалить ключ. null — провайдера нет.</summary>
public sealed record AgentProviderUpdateCommand(Guid ActorId, Guid ProviderId, string? Name, string? BaseUrl,
    IReadOnlyList<AgentModelInfo>? Models, string? ApiKey, bool ClearApiKey) : IRequest<AgentProviderResponse?>;

/// <summary>
/// Удалить провайдера: ключ стирается и в OpenCode, кастомный провайдер там выключается; если на нём стояла модель
/// агента — она возвращается к модели из конфигурации хоста. false — нет такого.
/// </summary>
public sealed record AgentProviderDeleteCommand(Guid ActorId, Guid ProviderId) : IRequest<bool>;

/// <summary>Модель, на которой Flow открывает сессии; оба null — модель из конфигурации хоста.</summary>
public sealed record AgentDefaultModelSetCommand(Guid ActorId, string? ProviderKey, string? ModelId) : IRequest<AgentStatusResponse>;

/// <summary>Пробный короткий запрос к модели (по умолчанию — к модели агента). Отказ — Ok = false с причиной, не исключение.</summary>
public sealed record AgentCheckCommand(Guid ActorId, string? ProviderKey, string? ModelId) : IRequest<AgentCheckResponse>;

/// <summary>Передать конфигурацию в OpenCode заново — кнопка «Применить» после того, как OpenCode стал доступен.</summary>
public sealed record AgentApplyCommand(Guid ActorId) : IRequest<IReadOnlyList<AgentProviderResponse>>;

/// <summary>
/// Фоновая синхронизация (OpenCodeConfigWorker): без Force — только если OpenCode доступен и чего-то из Flow в нём нет
/// (контейнер пересоздан, прошлая передача не удалась). true — конфигурация передавалась.
/// </summary>
public sealed record AgentSyncCommand(bool Force) : IRequest<bool>;

/// <summary>Причины, по которым конфигурация провайдера не дошла до OpenCode (ApplyError).</summary>
public static class AgentApplyErrors
{
    public const string Unreachable = "OpenCode недоступен: настройки сохранены во Flow и будут переданы, как только он ответит.";
    public const string UndecryptableKey = "Ключ не расшифровывается (сменились ключи DataProtection) — введите его заново.";
}

/// <summary>Передача провайдеров и ключей в OpenCode; общий путь для команд и фоновой синхронизации.</summary>
internal sealed class AgentConfigSync(
    IAgentProviderRepository providers,
    IAgentSecretProtector protector,
    IAgentRuntime runtime,
    IUnitOfWork unitOfWork)
{
    public bool NeedsKey(AgentProvider provider) =>
        provider.ApiKeyProtected is { } key && protector.TryUnprotect(key) is null;

    /// <summary>Провайдер, который OpenCode обязан видеть подключённым после передачи.</summary>
    public bool ExpectedConnected(AgentProvider provider) =>
        provider.IsCustom || provider.ApiKeyProtected is not null && !NeedsKey(provider);

    /// <summary>Передать всё в OpenCode и записать итог каждому провайдеру. false — OpenCode недоступен.</summary>
    public async Task<bool> ApplyAsync(IReadOnlyCollection<string> removedKeys, CancellationToken cancellationToken)
    {
        var all = await providers.GetAllAsync(cancellationToken);
        var configs = all.Select(p => new AgentProviderConfig(p.ProviderKey, p.Name, p.IsCustom, p.BaseUrl,
            p.Models.Select(m => (m.Id, m.Name)).ToList(),
            p.ApiKeyProtected is { } key ? protector.TryUnprotect(key) : null)).ToList();

        IReadOnlyDictionary<string, AgentApplyOutcome>? result = null;
        try
        {
            result = await runtime.ApplyAsync(configs, removedKeys, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException
                                   || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Недоступность — не ошибка команды: настройки уже сохранены, передачу повторит синхронизация.
        }

        var now = DateTime.UtcNow;
        foreach (var provider in all)
        {
            var outcome = result?.GetValueOrDefault(provider.ProviderKey) ?? AgentApplyOutcome.Ok;
            provider.MarkApplied(result is null ? AgentApplyErrors.Unreachable
                : NeedsKey(provider) ? AgentApplyErrors.UndecryptableKey
                : outcome.Error, outcome.RestartRequired, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result is not null;
    }

    public async Task<bool> SyncAsync(bool force, CancellationToken cancellationToken)
    {
        if (!force)
        {
            var state = await runtime.GetStateAsync(cancellationToken);
            if (!state.Reachable)
                return false;
            var all = await providers.GetAllAsync(cancellationToken);
            // Ждущий перезапуска OpenCode провайдер передавать заново незачем, пока OpenCode его не увидит: тогда
            // повторная передача снимет отметку. Остальные — заново, если их нет (контейнер пересоздан).
            var inSync = all.All(p => p.AppliedAt is not null && p.ApplyError is null
                                      && (!ExpectedConnected(p) || p.RestartRequired != state.Connected.Contains(p.ProviderKey)));
            if (inSync)
                return false;
        }

        return await ApplyAsync([], cancellationToken);
    }

    public async Task<AgentModelRef> DefaultModelAsync(CancellationToken cancellationToken) =>
        await providers.GetSettingsAsync(cancellationToken) is { ProviderKey: { } key, ModelId: { } model }
            ? new AgentModelRef(key, model)
            : runtime.ConfiguredModel;
}

internal sealed class AgentSettingsHandlers(
    IAgentProviderRepository providers,
    IAgentSecretProtector protector,
    IAgentRuntime runtime,
    AgentConfigSync sync,
    ActorResolver actors,
    IPermissionService permissions,
    IUnitOfWork unitOfWork) :
    IRequestHandler<AgentStatusQuery, AgentStatusResponse>,
    IRequestHandler<AgentCatalogQuery, IReadOnlyList<AgentCatalogProviderResponse>>,
    IRequestHandler<AgentProviderListQuery, IReadOnlyList<AgentProviderResponse>>,
    IRequestHandler<AgentProviderCreateCommand, AgentProviderResponse>,
    IRequestHandler<AgentProviderUpdateCommand, AgentProviderResponse?>,
    IRequestHandler<AgentProviderDeleteCommand, bool>,
    IRequestHandler<AgentDefaultModelSetCommand, AgentStatusResponse>,
    IRequestHandler<AgentCheckCommand, AgentCheckResponse>,
    IRequestHandler<AgentApplyCommand, IReadOnlyList<AgentProviderResponse>>,
    IRequestHandler<AgentSyncCommand, bool>
{
    public const int ApiKeyMaxLength = 4096;
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(120);

    private async Task<User> AdminAsync(Guid actorId, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(actorId, cancellationToken);
        permissions.EnsureCanManageAgent(actor);
        return actor;
    }

    public async Task<AgentStatusResponse> Handle(AgentStatusQuery request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        return await StatusAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentCatalogProviderResponse>> Handle(AgentCatalogQuery request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        IReadOnlyList<AgentCatalogEntry> catalog;
        try
        {
            catalog = await runtime.GetCatalogAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            throw new InvalidOperationException("OpenCode недоступен — каталог провайдеров появится, когда он ответит. Кастомного провайдера можно добавить и сейчас.", ex);
        }

        return catalog
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new AgentCatalogProviderResponse(c.Id, c.Name, c.Env, c.Models.Select(m => new AgentModelInfo(m.Id, m.Name)).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<AgentProviderResponse>> Handle(AgentProviderListQuery request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        return await ListAsync(cancellationToken);
    }

    public async Task<AgentProviderResponse> Handle(AgentProviderCreateCommand request, CancellationToken cancellationToken)
    {
        var actor = await AdminAsync(request.ActorId, cancellationToken);
        var key = AgentProvider.NormalizeKey(request.ProviderKey);
        if (await providers.ExistsByKeyAsync(key, cancellationToken))
            throw new InvalidOperationException($"Провайдер «{key}» уже подключён — измените его.");

        var entry = await CatalogEntryAsync(key, cancellationToken);
        AgentProvider provider;
        if (request.IsCustom)
        {
            if (entry is not null)
                throw new ArgumentException($"Id «{key}» занят провайдером «{entry.Name}» из каталога OpenCode — выберите другой.", nameof(request.ProviderKey));
            provider = AgentProvider.CreateCustom(key, string.IsNullOrWhiteSpace(request.Name) ? key : request.Name, request.BaseUrl ?? string.Empty,
                Models(request.Models), actor.Id);
        }
        else
        {
            if (entry is null && await CatalogAvailableAsync(cancellationToken))
                throw new ArgumentException($"OpenCode не знает провайдера «{key}». Для своего сервера подключите кастомный провайдер.", nameof(request.ProviderKey));
            provider = AgentProvider.CreateCatalog(key, entry?.Name ?? (string.IsNullOrWhiteSpace(request.Name) ? key : request.Name), actor.Id);
        }

        if (ApiKey(request.ApiKey) is { } apiKey)
            provider.SetKey(protector.Protect(apiKey), AgentProvider.HintOf(apiKey), actor.Id, DateTime.UtcNow);

        providers.Add(provider);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await sync.ApplyAsync([], cancellationToken);
        return await ResponseAsync(provider, cancellationToken);
    }

    public async Task<AgentProviderResponse?> Handle(AgentProviderUpdateCommand request, CancellationToken cancellationToken)
    {
        var actor = await AdminAsync(request.ActorId, cancellationToken);
        var provider = await providers.GetByIdAsync(request.ProviderId, cancellationToken);
        if (provider is null)
            return null;

        if (provider.IsCustom)
        {
            if (request.Name is not null || request.BaseUrl is not null || request.Models is not null)
                provider.UpdateCustom(request.Name ?? provider.Name, request.BaseUrl ?? provider.BaseUrl,
                    request.Models is null ? provider.Models.Select(m => (m.Id, m.Name)) : Models(request.Models));
        }
        else
        {
            if (request.BaseUrl is not null || request.Models is not null)
                throw new ArgumentException("Адрес и модели провайдера из каталога задаёт OpenCode.", nameof(request.BaseUrl));
            if (request.Name is not null)
                provider.Rename(request.Name);
        }

        var now = DateTime.UtcNow;
        if (ApiKey(request.ApiKey) is { } apiKey)
            provider.SetKey(protector.Protect(apiKey), AgentProvider.HintOf(apiKey), actor.Id, now);
        else if (request.ClearApiKey)
            provider.ClearKey(actor.Id, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        // Ключ удалён во Flow — стереть и копию в OpenCode: передача ниже шлёт DELETE для провайдера без ключа.
        await sync.ApplyAsync([], cancellationToken);
        return await ResponseAsync(provider, cancellationToken);
    }

    public async Task<bool> Handle(AgentProviderDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await AdminAsync(request.ActorId, cancellationToken);
        var provider = await providers.GetByIdAsync(request.ProviderId, cancellationToken);
        if (provider is null)
            return false;

        providers.Remove(provider);
        // Иначе Flow открывал бы сессии на провайдере, которого больше нет.
        if (await providers.GetSettingsAsync(cancellationToken) is { } settings && settings.ProviderKey == provider.ProviderKey)
            settings.SetDefaultModel(null, null, actor.Id, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await sync.ApplyAsync([provider.ProviderKey], cancellationToken);
        return true;
    }

    public async Task<AgentStatusResponse> Handle(AgentDefaultModelSetCommand request, CancellationToken cancellationToken)
    {
        var actor = await AdminAsync(request.ActorId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.ProviderKey) && !string.IsNullOrWhiteSpace(request.ModelId))
        {
            var key = AgentProvider.NormalizeKey(request.ProviderKey);
            var custom = (await providers.GetAllAsync(cancellationToken)).FirstOrDefault(p => p.IsCustom && p.ProviderKey == key);
            if (custom is not null && custom.Models.All(m => m.Id != request.ModelId.Trim()))
                throw new ArgumentException($"У провайдера «{custom.Name}» нет модели «{request.ModelId.Trim()}».", nameof(request.ModelId));
        }

        var settings = await providers.GetSettingsAsync(cancellationToken);
        if (settings is null)
        {
            settings = AgentSettings.Create();
            providers.AddSettings(settings);
        }

        settings.SetDefaultModel(request.ProviderKey, request.ModelId, actor.Id, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await StatusAsync(cancellationToken);
    }

    public async Task<AgentCheckResponse> Handle(AgentCheckCommand request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        var model = !string.IsNullOrWhiteSpace(request.ProviderKey) && !string.IsNullOrWhiteSpace(request.ModelId)
            ? new AgentModelRef(AgentProvider.NormalizeKey(request.ProviderKey), request.ModelId.Trim())
            : await sync.DefaultModelAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CheckTimeout);
        try
        {
            var answer = (await runtime.CheckAsync(model, timeout.Token)).Trim();
            var preview = answer.Length <= 200 ? answer : answer[..200] + "…";
            return new AgentCheckResponse(true, model.ProviderKey, model.ModelId, $"Модель ответила: {preview}");
        }
        catch (HttpRequestException ex)
        {
            return new AgentCheckResponse(false, model.ProviderKey, model.ModelId, ex.Message);
        }
        catch (Exception ex) when (ex is TimeoutException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new AgentCheckResponse(false, model.ProviderKey, model.ModelId,
                $"Модель не ответила за {CheckTimeout.TotalSeconds:0} с: проверьте ключ, адрес провайдера и доступ OpenCode в интернет.");
        }
        catch (InvalidOperationException ex)
        {
            return new AgentCheckResponse(false, model.ProviderKey, model.ModelId, ex.Message);
        }
    }

    public async Task<IReadOnlyList<AgentProviderResponse>> Handle(AgentApplyCommand request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        await sync.ApplyAsync([], cancellationToken);
        return await ListAsync(cancellationToken);
    }

    public Task<bool> Handle(AgentSyncCommand request, CancellationToken cancellationToken) =>
        sync.SyncAsync(request.Force, cancellationToken);

    private async Task<AgentStatusResponse> StatusAsync(CancellationToken cancellationToken)
    {
        var state = await runtime.GetStateAsync(cancellationToken);
        var settings = await providers.GetSettingsAsync(cancellationToken);
        var fromSettings = settings is { ProviderKey: not null, ModelId: not null };
        var model = fromSettings ? new AgentModelRef(settings!.ProviderKey!, settings.ModelId!) : runtime.ConfiguredModel;
        var restart = (await providers.GetAllAsync(cancellationToken)).Where(p => p.RestartRequired).Select(p => p.Name).ToList();
        return new AgentStatusResponse(state.Reachable, state.Version, state.Error, state.Connected.Order(StringComparer.Ordinal).ToList(),
            model.ProviderKey, model.ModelId, fromSettings, state.Reachable && state.Connected.Contains(model.ProviderKey), restart);
    }

    private async Task<IReadOnlyList<AgentProviderResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var state = await runtime.GetStateAsync(cancellationToken);
        return (await providers.GetAllAsync(cancellationToken))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => Response(p, state.Connected))
            .ToList();
    }

    private async Task<AgentProviderResponse> ResponseAsync(AgentProvider provider, CancellationToken cancellationToken) =>
        Response(provider, (await runtime.GetStateAsync(cancellationToken)).Connected);

    private AgentProviderResponse Response(AgentProvider p, IReadOnlyCollection<string> connected) =>
        new(p.Id, p.ProviderKey, p.Name, p.IsCustom, p.BaseUrl, p.Models.Select(m => new AgentModelInfo(m.Id, m.Name)).ToList(),
            p.ApiKeyProtected is not null, p.KeyHint, p.KeyUpdatedAt, p.KeyUpdatedById, sync.NeedsKey(p), p.AppliedAt, p.ApplyError,
            connected.Contains(p.ProviderKey), p.RestartRequired);

    /// <summary>Провайдер каталога OpenCode; null — нет такого или каталог недоступен.</summary>
    private async Task<AgentCatalogEntry?> CatalogEntryAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return (await runtime.GetCatalogAsync(cancellationToken)).FirstOrDefault(c => c.Id == key);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            return null;
        }
    }

    private async Task<bool> CatalogAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            return (await runtime.GetCatalogAsync(cancellationToken)).Count > 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            return false;
        }
    }

    private static IEnumerable<(string Id, string Name)> Models(IReadOnlyList<AgentModelInfo>? models) =>
        (models ?? []).Select(m => (m.Id, m.Name));

    /// <summary>Ключ из формы: пусто — нет ключа; переводы строк и пробелы внутри — опечатка копирования, а не ключ.</summary>
    private static string? ApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;
        var key = apiKey.Trim();
        if (key.Length > ApiKeyMaxLength || key.Any(char.IsWhiteSpace))
            throw new ArgumentException($"Ключ API — одна строка без пробелов, до {ApiKeyMaxLength} символов.", nameof(apiKey));
        return key;
    }
}
