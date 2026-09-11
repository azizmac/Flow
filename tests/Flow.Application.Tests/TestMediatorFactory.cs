using Flow.Application.Abstractions;
using Flow.Application.DependencyInjection;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Flow.Application.Tests;

/// <summary>
/// Собирает реальный DI-контейнер с AddFlowApplication() (тот же вызов, что и в Flow.Api/Program.cs) поверх
/// фейковых репозиториев — тесты идут через настоящий IMediator, а не напрямую дёргают internal-хендлеры.
/// </summary>
public static class TestMediatorFactory
{
    /// <summary>Owner, который сеется в FakeUserRepository при создании: actor для команд в тестах, где права не проверяются.</summary>
    public static readonly Guid OwnerId = Guid.Parse("00000000-0000-0000-0000-00000000aaaa");

    public static (IMediator Mediator, FakeBoardRepository Boards, FakeTaskItemRepository Tasks, FakeUserRepository Users) Create()
    {
        var (mediator, boards, tasks, users, _) = CreateWithAccounts();
        return (mediator, boards, tasks, users);
    }

    /// <summary>Плюс фейки ленты — для тестов активности и комментариев.</summary>
    public static (IMediator Mediator, FakeBoardRepository Boards, FakeTaskItemRepository Tasks, FakeUserRepository Users, FakeTaskCommentRepository Comments, FakeTaskActivityRepository Activities) CreateWithTimeline()
    {
        var all = Build();
        return (all.Mediator, all.Boards, all.Tasks, all.Users, all.Comments, all.Activities);
    }

    /// <summary>То же, плюс FakeAccountService — для тестов, которым важно, что ушло в Flow.Auth.</summary>
    public static (IMediator Mediator, FakeBoardRepository Boards, FakeTaskItemRepository Tasks, FakeUserRepository Users, FakeAccountService Accounts) CreateWithAccounts()
    {
        var all = Build();
        return (all.Mediator, all.Boards, all.Tasks, all.Users, all.Accounts);
    }

    private static (IMediator Mediator, FakeBoardRepository Boards, FakeTaskItemRepository Tasks, FakeUserRepository Users, FakeAccountService Accounts, FakeTaskCommentRepository Comments, FakeTaskActivityRepository Activities) Build()
    {
        var boards = new FakeBoardRepository();
        var tasks = new FakeTaskItemRepository();
        var users = new FakeUserRepository();
        var accounts = new FakeAccountService();
        var comments = new FakeTaskCommentRepository();
        var activities = new FakeTaskActivityRepository();

        var owner = User.CreateWithId(OwnerId, "owner", "owner@example.com", "Owner", "Flow");
        owner.ChangeRole(UserRole.Owner);
        owner.MarkActive();
        users.Add(owner);

        var services = new ServiceCollection();
        services.AddSingleton<IBoardRepository>(boards);
        services.AddSingleton<ITaskItemRepository>(tasks);
        services.AddSingleton<IUserRepository>(users);
        services.AddSingleton<IAccountService>(accounts);
        services.AddSingleton<ITaskCommentRepository>(comments);
        services.AddSingleton<ITaskActivityRepository>(activities);
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork());
        services.AddFlowApplication();

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
        return (mediator, boards, tasks, users, accounts, comments, activities);
    }
}
