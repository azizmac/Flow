using Flow.Shared.Contracts.About;
using MediatR;

namespace Flow.Application.Features.About.Queries.AboutQuery;

/// <summary>Версия, включённые модули и лимиты для экрана «Настройки → О системе». Читать может любая роль.</summary>
public sealed record AboutQuery : IRequest<AboutResponse>;
