using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserDeactivateCommand;

/// <summary>
/// Сначала блокировка входа в Flow.Auth (иначе деактивированный продолжит входить и обновлять токены),
/// потом статус в Users. Если Flow.Auth недоступен — AuthUnavailableException, статус не меняется.
/// </summary>
internal sealed class UserDeactivateCommandHandler(IUserRepository users, ISearchIndexQueue searchIndex, ActorResolver actors, IPermissionService permissions, IAccountService accounts, IUnitOfWork unitOfWork)
    : IRequestHandler<UserDeactivateCommand, UserUpdateResult>
{
    public async Task<UserUpdateResult> Handle(UserDeactivateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var user = actor.Id == request.UserId ? actor : await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return UserUpdateResult.NotFound();

        permissions.EnsureCanDeactivate(actor);

        // Инварианты домена проверяем до похода в Flow.Auth, иначе учётная запись окажется заблокирована при неизменном статусе.
        if (user.Role == UserRole.Owner)
            throw new InvalidOperationException($"User {user.Id} is an Owner; transfer ownership before deactivating.");

        if (!user.IsActive)
            throw new InvalidOperationException($"User {user.Id} is already deactivated.");

        await accounts.DisableAsync(user.Id, cancellationToken);

        user.Deactivate();

        // Ушедший человек из поиска убирается: назначать на него нельзя, и в выдаче он только мешает.
        // Профиль и история назначений остаются — удаляется проекция, а не данные.
        searchIndex.Enqueue(SearchSourceType.User, user.Id, boardId: null, SearchIndexOperation.Delete);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserUpdateResult.Success(user.ToResponse());
    }
}
