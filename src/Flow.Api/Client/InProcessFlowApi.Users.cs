using System.Net;
using Flow.Application.Features.Users;
using Flow.Application.Features.Users.Commands.UserActivateCommand;
using Flow.Application.Features.Users.Commands.UserChangeEmailCommand;
using Flow.Application.Features.Users.Commands.UserChangePasswordCommand;
using Flow.Application.Features.Users.Commands.UserChangeRoleCommand;
using Flow.Application.Features.Users.Commands.UserChangeUsernameCommand;
using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Flow.Application.Features.Users.Commands.UserDeactivateCommand;
using Flow.Application.Features.Users.Commands.UserRemoveLinkCommand;
using Flow.Application.Features.Users.Commands.UserSetLinkCommand;
using Flow.Application.Features.Users.Commands.UserUpdatePreferencesCommand;
using Flow.Application.Features.Users.Commands.UserUpdateProfileCommand;
using Flow.Application.Features.Users.Queries.UserGetByUsernameQuery;
using Flow.Application.Features.Users.Queries.UserGetMeQuery;
using Flow.Application.Features.Users.Queries.UserGetPreferencesQuery;
using Flow.Application.Features.Users.Queries.UserGetQuery;
using Flow.Application.Features.Users.Queries.UserListQuery;
using Flow.Application.Features.Users.Queries.UserSearchQuery;
using Flow.Client.Services;
using Flow.Shared.Contracts.Users;
using MediatR;

namespace Flow.Api.Client;

/// <summary>
/// Пользователи: срез UsersController. Удаления нет намеренно — только деактивация,
/// чтобы не терять историю назначений и упоминаний (см. docs/TZ_user.md).
///
/// Почти все изменяющие команды отдают общий <see cref="UserUpdateResult"/> с тремя исходами
/// (404 / 409 / 200) — их разбирает <see cref="FromUpdate"/>, ровно как ToActionResult в контроллере.
/// </summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<UserResponse>>> GetUsers(bool includeInactive = false, CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new UserListQuery(includeInactive), ct)));

    public Task<ApiResult<IReadOnlyList<UserResponse>>> SearchUsers(string query, int limit = 10, CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new UserSearchQuery(query, limit), ct)));

    /// <summary>
    /// Профиля может не быть при живой учётной записи (удалён руками) — контроллер отвечал на это 401,
    /// а не 404: экраны по 401 уводят на повторный вход, и менять это вместе с транспортом нельзя.
    /// </summary>
    public Task<ApiResult<UserResponse>> GetMe(CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var me = await mediator.Send(new UserGetMeQuery(actor), ct);

            return me is null
                ? ApiResult<UserResponse>.Fail("Профиль для этой учётной записи не найден.", HttpStatusCode.Unauthorized)
                : Ok(me);
        });

    public Task<ApiResult<UserResponse>> GetUser(Guid id, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new UserGetQuery(id), ct) is { } user ? Ok(user) : NotFound<UserResponse>());

    public Task<ApiResult<UserResponse>> GetUserByUsername(string username, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new UserGetByUsernameQuery(username), ct) is { } user ? Ok(user) : NotFound<UserResponse>());

    /// <summary>
    /// Пустой пароль контроллер отсекал до медиатора: учётную запись в Flow.Auth без начального пароля
    /// не создать, а сообщение об этом должно быть 400, а не отказом Flow.Auth где-то в глубине.
    /// </summary>
    public Task<ApiResult<UserResponse>> CreateUser(CreateUserRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            if (string.IsNullOrWhiteSpace(request.Password))
                return Invalid<UserResponse>("Password is required.");

            var actor = await ActorAsync();
            var result = await mediator.Send(
                new UserCreateCommand(
                    actor,
                    request.Username,
                    request.Email,
                    request.FirstName,
                    request.LastName,
                    request.Password!,
                    request.Role?.ToDomainRole()),
                ct);

            return result.IsConflict
                ? Conflict<UserResponse>(result.ConflictError!)
                : Ok(result.Response!);
        });

    public Task<ApiResult<UserResponse>> UpdateUserProfile(Guid id, UpdateUserProfileRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            return FromUpdate(await mediator.Send(
                new UserUpdateProfileCommand(
                    actor,
                    id,
                    request.FirstName,
                    request.LastName,
                    request.JobTitle,
                    request.Bio,
                    request.PhoneNumber,
                    request.AvatarUrl),
                ct));
        });

    public Task<ApiResult<UserResponse>> ChangeUsername(Guid id, ChangeUsernameRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            return FromUpdate(await mediator.Send(new UserChangeUsernameCommand(actor, id, request.Username), ct));
        });

    public Task<ApiResult<UserResponse>> ChangeEmail(Guid id, ChangeEmailRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            return FromUpdate(await mediator.Send(new UserChangeEmailCommand(actor, id, request.Email), ct));
        });

    /// <summary>
    /// Роль в команде хранится доменным перечислением, а по проводу шло перечисление Flow.Shared —
    /// поэтому ToDomainRole, как и в контроллере. Понижение последнего Owner — InvalidOperationException,
    /// её Guard превращает в 400.
    /// </summary>
    public Task<ApiResult<UserResponse>> ChangeUserRole(Guid id, ChangeUserRoleRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            return FromUpdate(await mediator.Send(new UserChangeRoleCommand(actor, id, request.Role.ToDomainRole()), ct));
        });

    /// <summary>
    /// Смена своего пароля (CurrentPassword задан) и сброс чужого (null, только Owner) — одна команда.
    /// Ответа с телом нет: контракт — true/false, где false означает «пользователя нет» (404 у SendNoContent).
    /// </summary>
    public Task<ApiResult<bool>> ChangePassword(Guid id, ChangePasswordRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var result = await mediator.Send(
                new UserChangePasswordCommand(actor, id, request.CurrentPassword, request.NewPassword),
                ct);

            return result.IsNotFound ? Missing() : Ok(true);
        });

    public Task<ApiResult<UserResponse>> SetUserLink(Guid id, UserLinkType type, SetUserLinkRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            return FromUpdate(await mediator.Send(new UserSetLinkCommand(actor, id, type, request.Url), ct));
        });

    public Task<ApiResult<bool>> RemoveUserLink(Guid id, UserLinkType type, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var result = await mediator.Send(new UserRemoveLinkCommand(actor, id, type), ct);

            return result.IsNotFound ? Missing() : Ok(true);
        });

    /// <summary>Повторная деактивация — InvalidOperationException → 400; отсутствие пользователя — false, а не ошибка.</summary>
    public Task<ApiResult<bool>> DeactivateUser(Guid id, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var result = await mediator.Send(new UserDeactivateCommand(actor, id), ct);

            return result.IsNotFound ? Missing() : Ok(true);
        });

    public Task<ApiResult<bool>> ActivateUser(Guid id, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var result = await mediator.Send(new UserActivateCommand(actor, id), ct);

            return result.IsNotFound ? Missing() : Ok(true);
        });

    public Task<ApiResult<UserPreferencesResponse>> GetPreferences(CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            return Ok(await mediator.Send(new UserGetPreferencesQuery(actor), ct));
        });

    public Task<ApiResult<UserPreferencesResponse>> UpdatePreferences(UpdateUserPreferencesRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            return Ok(await mediator.Send(
                new UserUpdatePreferencesCommand(actor, request.SidebarMode, request.StartPage, request.TasksPageSize),
                ct));
        });

    /// <summary>Три исхода UserUpdateResult — те же, что раскладывал ToActionResult контроллера.</summary>
    private static ApiResult<UserResponse> FromUpdate(UserUpdateResult result)
    {
        if (result.IsNotFound)
            return NotFound<UserResponse>();

        return result.ConflictError is { } conflict
            ? Conflict<UserResponse>(conflict)
            : Ok(result.Response!);
    }
}
