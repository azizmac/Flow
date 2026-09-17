using Flow.Application.Abstractions;
using Flow.Application.DependencyInjection;
using Flow.Application.Features.Attachments;
using Flow.Application.Features.Search;
using Flow.Application.Tests.Fakes;
using Flow.Auth.Contracts;
using Flow.Domain.Entities;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Flow.Application.Tests;

/// <summary>
/// Собирает реальный DI-контейнер с AddFlowApplication() (тот же вызов, что и в Flow.Api/Program.cs) поверх
/// фейковых репозиториев — тесты идут через настоящий IMediator, а не напрямую дёргают internal-хендлеры.
/// </summary>
public sealed record AttachmentTestContext(
    IMediator Mediator,
    FakeBoardRepository Boards,
    FakeTaskItemRepository Tasks,
    FakeUserRepository Users,
    FakeTaskActivityRepository Activities,
    FakeAttachmentRepository Attachments,
    InMemoryFileStorage Storage,
    FakeSearchIndexQueue SearchIndex,
    AttachmentOptions Options);

public sealed record SearchTestContext(
    IMediator Mediator,
    SearchOptions Options,
    FakeSearchQueryRepository Index,
    FakeQueryEmbeddingCache Embeddings,
    FakeBoardRepository Boards,
    FakeTaskItemRepository Tasks,
    FakeUserRepository Users,
    FakeReranker Reranker,
    FakeVisionEmbeddingGenerator Vision);

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

    /// <summary>Плюс очередь индексации — для тестов о том, что попадает в поисковый индекс.</summary>
    public static (IMediator Mediator, FakeBoardRepository Boards, FakeTaskItemRepository Tasks, FakeUserRepository Users, FakeTaskCommentRepository Comments, FakeSearchIndexQueue SearchIndex) CreateWithSearchIndex()
    {
        var all = Build();
        return (all.Mediator, all.Boards, all.Tasks, all.Users, all.Comments, all.SearchQueue);
    }

    /// <summary>Поисковая выдача: критерии, с которыми хендлер идёт в репозиторий, настройки и кэш векторов.</summary>
    public static (IMediator Mediator, SearchOptions Options, FakeSearchQueryRepository Index, FakeQueryEmbeddingCache Embeddings) CreateWithSearch()
    {
        var all = Build();
        return (all.Mediator, all.SearchOptions, all.SearchIndex, all.Embeddings);
    }

    /// <summary>Плюс вложения: репозиторий, хранилище и лимиты.</summary>
    public static AttachmentTestContext CreateAttachmentContext()
    {
        var all = Build();
        return new AttachmentTestContext(all.Mediator, all.Boards, all.Tasks, all.Users, all.Activities, all.Attachments, all.Storage, all.SearchQueue, all.AttachmentOptions);
    }

    /// <summary>
    /// Всё, что нужно тестам умного поиска: фейки репозиториев для сидирования проектов, задач
    /// и людей — плюс критерии, с которыми хендлер пришёл в индекс. Кортеж на семь элементов
    /// уже нечитаем, поэтому запись.
    /// </summary>
    public static SearchTestContext CreateSearchContext()
    {
        var all = Build();
        return new SearchTestContext(all.Mediator, all.SearchOptions, all.SearchIndex, all.Embeddings, all.Boards, all.Tasks, all.Users, all.Reranker, all.Vision);
    }

    /// <summary>То же, плюс FakeAccountService — для тестов, которым важно, что ушло в Flow.Auth.</summary>
    public static (IMediator Mediator, FakeBoardRepository Boards, FakeTaskItemRepository Tasks, FakeUserRepository Users, FakeAccountService Accounts) CreateWithAccounts()
    {
        var all = Build();
        return (all.Mediator, all.Boards, all.Tasks, all.Users, all.Accounts);
    }

    private static (IMediator Mediator, FakeBoardRepository Boards, FakeTaskItemRepository Tasks, FakeUserRepository Users, FakeAccountService Accounts, FakeTaskCommentRepository Comments, FakeTaskActivityRepository Activities, FakeSearchIndexQueue SearchQueue, SearchOptions SearchOptions, FakeSearchQueryRepository SearchIndex, FakeQueryEmbeddingCache Embeddings, FakeAttachmentRepository Attachments, InMemoryFileStorage Storage, AttachmentOptions AttachmentOptions, FakeReranker Reranker, FakeVisionEmbeddingGenerator Vision) Build()
    {
        var boards = new FakeBoardRepository();
        var tasks = new FakeTaskItemRepository();
        var users = new FakeUserRepository();
        var accounts = new FakeAccountService();
        var comments = new FakeTaskCommentRepository();
        var activities = new FakeTaskActivityRepository();
        var searchQueue = new FakeSearchIndexQueue();
        var searchOptions = new SearchOptions { Enabled = true };
        var attachments = new FakeAttachmentRepository();
        var storage = new InMemoryFileStorage();
        var attachmentOptions = new AttachmentOptions();
        var searchIndex = new FakeSearchQueryRepository();
        var reranker = new FakeReranker();
        var visionEmbedder = new FakeVisionEmbeddingGenerator();
        var embedder = new FakeEmbeddingGenerator();
        var embeddings = new FakeQueryEmbeddingCache(embedder);

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
        services.AddSingleton<IAttachmentRepository>(attachments);
        services.AddSingleton<IFileStorage>(storage);
        services.AddSingleton(attachmentOptions);
        services.AddSingleton<ISearchIndexQueue>(searchQueue);
        services.AddSingleton<ISearchIndexRepository>(new FakeSearchIndexRepository());
        services.AddSingleton<ISearchQueryRepository>(searchIndex);
        services.AddSingleton<IQueryEmbeddingCache>(embeddings);
        services.AddSingleton<IReranker>(reranker);
        services.AddSingleton<IVisionEmbeddingGenerator>(visionEmbedder);
        services.AddSingleton<IEmbeddingGenerator>(embedder);
        // Поиск включён: иначе /search/reindex отвечал бы «выключено» раньше проверки прав.
        services.AddSingleton(searchOptions);
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork());
        services.AddFlowApplication();

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
        return (mediator, boards, tasks, users, accounts, comments, activities, searchQueue, searchOptions, searchIndex, embeddings, attachments, storage, attachmentOptions, reranker, visionEmbedder);
    }
}
