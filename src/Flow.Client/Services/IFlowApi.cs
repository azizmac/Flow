using System.Net;
using Flow.Shared.Contracts.About;
using Flow.Shared.Contracts.Attachments;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Contracts.Users;
using Microsoft.AspNetCore.Components.Forms;

namespace Flow.Client.Services;

/// <summary>
/// Результат вызова API: либо значение, либо человекочитаемая ошибка. Коды остались от HTTP-транспорта
/// намеренно — на Ok/NotFound/Conflict/Unauthorized/Forbidden завязаны все экраны, и менять их вместе
/// с транспортом значило бы переписывать обработку ошибок во всём интерфейсе разом.
/// </summary>
public sealed record ApiResult<T>(T? Value, string? Error, HttpStatusCode Status)
{
    public bool Ok => Error is null;
    public bool NotFound => Status == HttpStatusCode.NotFound;
    public bool Conflict => Status == HttpStatusCode.Conflict;
    public bool Unauthorized => Status == HttpStatusCode.Unauthorized;
    public bool Forbidden => Status == HttpStatusCode.Forbidden;

    public static ApiResult<T> Success(T value, HttpStatusCode status) => new(value, null, status);
    public static ApiResult<T> Fail(string error, HttpStatusCode status) => new(default, error, status);
}

/// <summary>
/// Контракт доступа к данным Flow со стороны интерфейса. Живёт в Flow.Client, потому что им пользуются
/// страницы; реализация — нет: при серверном рендере она ходит в Flow.Application через IMediator,
/// а Flow.Client по слоям видит только Flow.Shared (см. AGENTS.md, «Слойная архитектура»).
/// Поэтому InProcessFlowApi лежит в Flow.Api, а страницы инжектят именно интерфейс.
///
/// Семантика ApiResult сохранена как была у HTTP-клиента: Ok/NotFound/Conflict/Unauthorized/Forbidden —
/// на них завязаны все экраны, и менять её вместе с транспортом нельзя.
/// </summary>
public interface IFlowApi
{
    // ---- Проекты ----
    Task<ApiResult<IReadOnlyList<BoardResponse>>> GetBoards(CancellationToken ct = default);
    Task<ApiResult<BoardResponse>> GetBoard(Guid id, CancellationToken ct = default);
    Task<ApiResult<BoardResponse>> CreateBoard(CreateBoardRequest request, CancellationToken ct = default);
    Task<ApiResult<BoardResponse>> RenameBoard(Guid id, RenameBoardRequest request, CancellationToken ct = default);
    Task<ApiResult<bool>> DeleteBoard(Guid id, CancellationToken ct = default);

    // ---- Задачи ----
    Task<ApiResult<IReadOnlyList<TaskResponse>>> GetTasks(Guid boardId, Guid? assigneeId = null, CancellationToken ct = default);

    Task<ApiResult<TaskListResponse>> SearchTasks(
        Guid? boardId = null,
        Guid? assigneeId = null,
        bool unassigned = false,
        Guid? statusId = null,
        StatusType? statusType = null,
        string? query = null,
        int? limit = null,
        string? cursor = null,
        int? offset = null,
        TaskSortField? sort = null,
        bool descending = false,
        CancellationToken ct = default);

    Task<ApiResult<TaskResponse>> GetTask(Guid id, CancellationToken ct = default);
    Task<ApiResult<TaskResponse>> CreateTask(Guid boardId, CreateTaskRequest request, CancellationToken ct = default);
    Task<ApiResult<TaskResponse>> UpdateTask(Guid id, UpdateTaskRequest request, CancellationToken ct = default);
    Task<ApiResult<bool>> DeleteTask(Guid id, CancellationToken ct = default);
    Task<ApiResult<TaskResponse>> AssignTask(Guid id, AssignTaskRequest request, CancellationToken ct = default);
    Task<ApiResult<TaskResponse>> SetDueDate(Guid id, SetTaskDueDateRequest request, CancellationToken ct = default);

    // ---- Комментарии и журнал ----
    Task<ApiResult<IReadOnlyList<TaskCommentResponse>>> GetComments(Guid taskId, CancellationToken ct = default);
    Task<ApiResult<TaskCommentResponse>> AddComment(Guid taskId, CreateTaskCommentRequest request, CancellationToken ct = default);
    Task<ApiResult<TaskCommentResponse>> UpdateComment(Guid id, UpdateTaskCommentRequest request, CancellationToken ct = default);
    Task<ApiResult<bool>> DeleteComment(Guid id, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<TaskActivityResponse>>> GetActivity(Guid taskId, CancellationToken ct = default);

    // ---- Вложения ----
    Task<ApiResult<IReadOnlyList<AttachmentResponse>>> GetAttachments(Guid taskId, CancellationToken ct = default);
    Task<ApiResult<AttachmentResponse>> UploadAttachment(Guid taskId, IBrowserFile file, long maxBytes, CancellationToken ct = default);
    Task<ApiResult<bool>> DeleteAttachment(Guid id, CancellationToken ct = default);

    // ---- Поиск ----
    Task<ApiResult<SearchResponse>> Search(
        string query,
        IReadOnlyCollection<SearchSourceType>? types = null,
        Guid? boardId = null,
        bool includeArchived = false,
        int limit = 20,
        int offset = 0,
        bool rerank = false,
        CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<SearchResultItem>>> GetSimilarTasks(Guid taskId, int limit = 5, CancellationToken ct = default);
    Task<ApiResult<SearchStatusResponse>> GetSearchStatus(CancellationToken ct = default);

    /// <summary>Число поставленных в очередь источников. Только Owner; при выключенном поиске — 400.</summary>
    Task<ApiResult<int>> Reindex(ReindexRequest request, CancellationToken ct = default);

    // ---- Люди ----
    Task<ApiResult<IReadOnlyList<UserResponse>>> GetUsers(bool includeInactive = false, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<UserResponse>>> SearchUsers(string query, int limit = 10, CancellationToken ct = default);
    Task<ApiResult<UserResponse>> GetMe(CancellationToken ct = default);
    Task<ApiResult<UserResponse>> GetUser(Guid id, CancellationToken ct = default);
    Task<ApiResult<UserResponse>> GetUserByUsername(string username, CancellationToken ct = default);
    Task<ApiResult<UserResponse>> CreateUser(CreateUserRequest request, CancellationToken ct = default);
    Task<ApiResult<UserResponse>> UpdateUserProfile(Guid id, UpdateUserProfileRequest request, CancellationToken ct = default);
    Task<ApiResult<UserResponse>> ChangeUsername(Guid id, ChangeUsernameRequest request, CancellationToken ct = default);
    Task<ApiResult<UserResponse>> ChangeEmail(Guid id, ChangeEmailRequest request, CancellationToken ct = default);
    Task<ApiResult<UserResponse>> ChangeUserRole(Guid id, ChangeUserRoleRequest request, CancellationToken ct = default);
    Task<ApiResult<bool>> ChangePassword(Guid id, ChangePasswordRequest request, CancellationToken ct = default);
    Task<ApiResult<UserResponse>> SetUserLink(Guid id, UserLinkType type, SetUserLinkRequest request, CancellationToken ct = default);
    Task<ApiResult<bool>> RemoveUserLink(Guid id, UserLinkType type, CancellationToken ct = default);
    Task<ApiResult<bool>> DeactivateUser(Guid id, CancellationToken ct = default);
    Task<ApiResult<bool>> ActivateUser(Guid id, CancellationToken ct = default);
    Task<ApiResult<UserPreferencesResponse>> GetPreferences(CancellationToken ct = default);
    Task<ApiResult<UserPreferencesResponse>> UpdatePreferences(UpdateUserPreferencesRequest request, CancellationToken ct = default);

    // ---- О системе ----
    Task<ApiResult<AboutResponse>> GetAbout(CancellationToken ct = default);
}
