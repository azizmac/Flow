using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserCreateCommand;

/// <summary>Бросает ArgumentException, если поля не проходят валидацию (см. User.Create).</summary>
internal sealed class UserCreateCommandHandler(IUserRepository users, IUnitOfWork unitOfWork)
    : IRequestHandler<UserCreateCommand, UserCreateResult>
{
    public async Task<UserCreateResult> Handle(UserCreateCommand request, CancellationToken cancellationToken)
    {
        // User.Create нормализует username/email (trim + lower), поэтому уникальность проверяется
        // по user.Username / user.Email, а не по сырому запросу: "Ilya" и "ilya" — один пользователь.
        var user = User.Create(request.Username, request.Email, request.FirstName, request.LastName);

        if (await users.ExistsByUsernameAsync(user.Username, cancellationToken))
            return UserCreateResult.UsernameTaken(user.Username);

        if (await users.ExistsByEmailAsync(user.Email, cancellationToken))
            return UserCreateResult.EmailTaken(user.Email);

        users.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserCreateResult.Success(user.ToResponse());
    }
}
