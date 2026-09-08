using Flow.Application.Abstractions;
using Flow.Application.DependencyInjection;
using Flow.Application.Tests.Fakes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Flow.Application.Tests;

/// <summary>
/// Собирает реальный DI-контейнер с AddFlowApplication() (тот же вызов, что и в Flow.Api/Program.cs) поверх
/// фейковых репозиториев — тесты идут через настоящий IMediator, а не напрямую дёргают internal-хендлеры.
/// </summary>
public static class TestMediatorFactory
{
    public static (IMediator Mediator, FakeBoardRepository Boards, FakeTaskItemRepository Tasks) Create()
    {
        var boards = new FakeBoardRepository();
        var tasks = new FakeTaskItemRepository();

        var services = new ServiceCollection();
        services.AddSingleton<IBoardRepository>(boards);
        services.AddSingleton<ITaskItemRepository>(tasks);
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork());
        services.AddFlowApplication();

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
        return (mediator, boards, tasks);
    }
}
