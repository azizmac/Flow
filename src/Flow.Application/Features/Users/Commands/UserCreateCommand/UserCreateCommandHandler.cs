using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserCreateCommand;

/// <summary>Бросает ArgumentException, если поля не проходят валидацию (см. User.Create) или Flow.Auth отверг пароль.</summary>
internal sealed class UserCreateCommandHandler(IUserRepository users, IAccountService accounts, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
    : IRequestHandler<UserCreateCommand, UserCreateResult>
{
    public async Task<UserCreateResult> Handle(UserCreateCommand request, CancellationToken cancellationToken)
    {
        var role = request.Role ?? UserRole.Member;
        permissions.EnsureCanCreateUser(await actors.ResolveAsync(request.ActorId, cancellationToken), role);

        // User.Create нормализует username/email (trim + lower), поэтому уникальность проверяется
        // по user.Username / user.Email, а не по сырому запросу: "Ilya" и "ilya" — один пользователь.
        var user = User.Create(request.Username, request.Email, request.FirstName, request.LastName);
        user.ChangeRole(role);

        // Локальная копия — быстрый отказ без похода в Flow.Auth; источник истины всё равно там.
        if (await users.ExistsByUsernameAsync(user.Username, cancellationToken))
            return UserCreateResult.UsernameTaken(user.Username);

        if (await users.ExistsByEmailAsync(user.Email, cancellationToken))
            return UserCreateResult.EmailTaken(user.Email);

        // Сначала учётная запись, потом профиль: без учётной записи человек не сможет войти.
        var account = await accounts.CreateAsync(user.Id, user.Username, user.Email, request.Password, cancellationToken);
        switch (account.Status)
        {
            case AccountResultStatus.UsernameTaken:
                return UserCreateResult.UsernameTaken(user.Username);
            case AccountResultStatus.EmailTaken:
                return UserCreateResult.EmailTaken(user.Email);
            case AccountResultStatus.Invalid:
                throw new ArgumentException(account.Error ?? "Flow.Auth rejected the account.", nameof(request.Password));
        }

        users.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserCreateResult.Success(user.ToResponse());
    }
}
