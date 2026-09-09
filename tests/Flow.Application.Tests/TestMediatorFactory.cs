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
    public static (IMediator Mediator, FakeBoardRepository Boards, FakeTaskItemRepository Tasks, FakeUserRepository Users) Create()
    {
        var (mediator, boards, tasks, users, _) = CreateWithAccounts();
        return (mediator, boards, tasks, users);
    }

    /// <summary>То же, плюс FakeAccountService — для тестов, которым важно, что ушло в Flow.Auth.</summary>
    public static (IMediator Mediator, FakeBoardRepository Boards, FakeTaskItemRepository Tasks, FakeUserRepository Users, FakeAccountService Accounts) CreateWithAccounts()
    {
        var boards = new FakeBoardRepository();
        var tasks = new FakeTaskItemRepository();
        var users = new FakeUserRepository();
        var accounts = new FakeAccountService();

        var services = new ServiceCollection();
        services.AddSingleton<IBoardRepository>(boards);
        services.AddSingleton<ITaskItemRepository>(tasks);
        services.AddSingleton<IUserRepository>(users);
        services.AddSingleton<IAccountService>(accounts);
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork());
        services.AddFlowApplication();

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
        return (mediator, boards, tasks, users, accounts);
    }
}
