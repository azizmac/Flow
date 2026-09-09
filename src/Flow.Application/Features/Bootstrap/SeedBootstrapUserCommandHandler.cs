using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Bootstrap;

internal sealed class SeedBootstrapUserCommandHandler(IUserRepository users, IUnitOfWork unitOfWork)
    : IRequestHandler<SeedBootstrapUserCommand, bool>
{
    public async Task<bool> Handle(SeedBootstrapUserCommand request, CancellationToken cancellationToken)
    {
        if (await users.GetByIdAsync(request.Id, cancellationToken) is not null)
            return false;

        var user = User.CreateWithId(request.Id, request.Username, request.Email, request.FirstName, request.LastName);

        users.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
