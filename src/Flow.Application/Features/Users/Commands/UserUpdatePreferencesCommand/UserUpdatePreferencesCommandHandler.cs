using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Application.Features.Users.Commands.UserUpdatePreferencesCommand;

/// <summary>Ни журнала, ни очереди индексации: настройки не видны никому, кроме самого человека.</summary>
internal sealed class UserUpdatePreferencesCommandHandler(ActorResolver actors, IUnitOfWork unitOfWork)
    : IRequestHandler<UserUpdatePreferencesCommand, UserPreferencesResponse>
{
    public async Task<UserPreferencesResponse> Handle(UserUpdatePreferencesCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var preferences = actor.Preferences.With(
            request.SidebarMode?.ToDomainSidebarMode(),
            request.StartPage?.ToDomainStartPage(),
            request.TasksPageSize);

        if (actor.ChangePreferences(preferences))
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return actor.Preferences.ToResponse();
    }
}
