using Flow.Application.Abstractions;
using Flow.Application.Security;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Flow.Application.DependencyInjection;

public static class FlowApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует MediatR и сканирует эту сборку на IRequestHandler&lt;,&gt; — благодаря сканированию
    /// хендлеры фич (Features/*/Commands|Queries/*) могут оставаться internal: MediatR находит их
    /// рефлексией независимо от модификатора доступа, а Flow.Api работает только через IMediator
    /// и публичные команды/запросы. Плюс права: IPermissionService и ActorResolver (docs/TZ_user_roles.md).
    /// </summary>
    public static IServiceCollection AddFlowApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(FlowApplicationServiceCollectionExtensions).Assembly));

        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<ActorResolver>();

        return services;
    }
}
