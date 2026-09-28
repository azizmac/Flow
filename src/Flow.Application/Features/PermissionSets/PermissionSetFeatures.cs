using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using MediatR;
using DomainPermission = Flow.Domain.Entities.ProjectPermission;
using DomainRole = Flow.Domain.Entities.ProjectRole;
using SharedPermission = Flow.Shared.Contracts.Boards.ProjectPermission;

namespace Flow.Application.Features.PermissionSets;

// Наборы прав (docs/TZ_project_access.md §7, этап 4E). Встроенные — четыре роли проекта: живут в коде (ProjectRoles),
// в ответе идут первыми с IsBuiltIn и постоянными Id, не правятся и не удаляются — свой набор делают клонированием.
// Читают все (выбор набора в «Доступе»), правит матрицу только Owner. Удаление набора возвращает его участиям
// права базовой роли (FK SetNull), роль участия при этом не меняется.

public sealed record PermissionSetListQuery(Guid ActorId) : IRequest<IReadOnlyList<PermissionSetResponse>>;

public sealed record PermissionSetCreateCommand(Guid ActorId, string Name, string? Description, DomainRole BaseRole, IReadOnlyList<DomainPermission> Permissions)
    : IRequest<PermissionSetResponse>;

/// <summary>Имя, описание и права; базовая роль не меняется. null — набора нет; встроенный — 400.</summary>
public sealed record PermissionSetUpdateCommand(Guid ActorId, Guid SetId, string Name, string? Description, IReadOnlyList<DomainPermission> Permissions)
    : IRequest<PermissionSetResponse?>;

public sealed record PermissionSetDeleteCommand(Guid ActorId, Guid SetId) : IRequest<bool>;

/// <summary>Права из контракта в домен: неизвестное число — 400 (домен проверит ещё раз).</summary>
public static class PermissionSetMapping
{
    public static IReadOnlyList<DomainPermission> ToDomain(IEnumerable<SharedPermission> permissions) =>
        permissions.Select(p => Enum.IsDefined((DomainPermission)(int)p) ? (DomainPermission)(int)p : throw new ArgumentException($"Unknown permission {p}.")).ToList();
}

/// <summary>Роль участия по набору: у своего набора — его базовая роль; неизвестный набор — 400.</summary>
internal static class PermissionSetRoles
{
    /// <summary>Постоянный Id встроенного набора роли — чтобы клиент выбирал роль и свой набор из одного списка.</summary>
    public static Guid BuiltInId(DomainRole role) => new($"0f10c000-0000-4000-8000-00000000000{(int)role}");

    /// <summary>Роль и набор участия: встроенный Id — просто роль, свой набор — его базовая роль.</summary>
    public static async Task<(DomainRole Role, Guid? SetId)> RoleOfAsync(IPermissionSetRepository sets, DomainRole role, Guid? setId, CancellationToken cancellationToken)
    {
        if (setId is not { } id)
            return (role, null);
        foreach (var builtIn in Enum.GetValues<DomainRole>())
            if (BuiltInId(builtIn) == id)
                return (builtIn, null);
        var set = await sets.GetByIdAsync(id, cancellationToken) ?? throw new InvalidOperationException("Набора прав нет.");
        return (set.BaseRole, set.Id);
    }
}

internal sealed class PermissionSetHandlers(
    IPermissionSetRepository sets,
    ActorResolver actors,
    IPermissionService permissions,
    IUnitOfWork unitOfWork) :
    IRequestHandler<PermissionSetListQuery, IReadOnlyList<PermissionSetResponse>>,
    IRequestHandler<PermissionSetCreateCommand, PermissionSetResponse>,
    IRequestHandler<PermissionSetUpdateCommand, PermissionSetResponse?>,
    IRequestHandler<PermissionSetDeleteCommand, bool>
{
    public async Task<IReadOnlyList<PermissionSetResponse>> Handle(PermissionSetListQuery request, CancellationToken cancellationToken)
    {
        await actors.ResolveAsync(request.ActorId, cancellationToken);
        var builtIn = Enum.GetValues<DomainRole>().Select(role => new PermissionSetResponse(
            PermissionSetRoles.BuiltInId(role), Security.RoleNames.Of(role), null, role.ToResponseRole(),
            ProjectRoles.PermissionsOf(role).OrderBy(p => p).Select(ToShared).ToList(), IsBuiltIn: true));
        return builtIn.Concat((await sets.GetAllAsync(cancellationToken)).Select(ToResponse)).ToList();
    }

    public async Task<PermissionSetResponse> Handle(PermissionSetCreateCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanManagePermissionSets(await actors.ResolveAsync(request.ActorId, cancellationToken));
        var set = PermissionSet.Create(request.Name, request.Description, request.BaseRole, request.Permissions);
        await EnsureNameFreeAsync(set.Name, null, cancellationToken);
        sets.Add(set);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(set);
    }

    public async Task<PermissionSetResponse?> Handle(PermissionSetUpdateCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanManagePermissionSets(await actors.ResolveAsync(request.ActorId, cancellationToken));
        if (Enum.GetValues<DomainRole>().Any(r => PermissionSetRoles.BuiltInId(r) == request.SetId))
            throw new InvalidOperationException("Встроенный набор не правится — склонируйте его.");
        var set = await sets.GetByIdAsync(request.SetId, cancellationToken);
        if (set is null)
            return null;

        set.Update(request.Name, request.Description, request.Permissions);
        await EnsureNameFreeAsync(set.Name, set.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(set);
    }

    public async Task<bool> Handle(PermissionSetDeleteCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanManagePermissionSets(await actors.ResolveAsync(request.ActorId, cancellationToken));
        if (Enum.GetValues<DomainRole>().Any(r => PermissionSetRoles.BuiltInId(r) == request.SetId))
            throw new InvalidOperationException("Встроенный набор не удаляется.");
        var set = await sets.GetByIdAsync(request.SetId, cancellationToken);
        if (set is null)
            return false;

        sets.Remove(set);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task EnsureNameFreeAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var taken = Enum.GetValues<DomainRole>().Any(r => string.Equals(Security.RoleNames.Of(r), name, StringComparison.OrdinalIgnoreCase))
                    || (await sets.GetAllAsync(cancellationToken)).Any(s => s.Id != exceptId && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (taken)
            throw new InvalidOperationException($"Набор «{name}» уже есть.");
    }

    private static PermissionSetResponse ToResponse(PermissionSet set) =>
        new(set.Id, set.Name, set.Description, set.BaseRole.ToResponseRole(), set.Permissions.Select(ToShared).ToList(), IsBuiltIn: false);

    private static SharedPermission ToShared(DomainPermission permission) => (SharedPermission)(int)permission;
}
