using System.Net;
using Flow.Shared.Contracts.About;
using Flow.Shared.Contracts.Attachments;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using Flow.Shared.Contracts.Sprints;
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

    /// <summary>Ошибка в строке FQL — с местом, чтобы подчеркнуть его; у остальных ошибок null.</summary>
    public Flow.Shared.Contracts.Filters.FqlErrorResponse? FqlError { get; init; }

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
    Task<ApiResult<BoardResponse>> CreateTaskType(Guid boardId, CreateTaskTypeRequest request, CancellationToken ct = default);
    Task<ApiResult<BoardResponse>> UpdateTaskType(Guid boardId, Guid typeId, UpdateTaskTypeRequest request, CancellationToken ct = default);
    Task<ApiResult<BoardResponse>> CreateStatus(Guid boardId, CreateStatusRequest request, CancellationToken ct = default);
    Task<ApiResult<BoardResponse>> UpdateStatus(Guid boardId, Guid statusId, UpdateStatusRequest request, CancellationToken ct = default);
    /// <summary>Удалить статус, переведя его задачи в <paramref name="moveTo"/> (в журнал задач — смена статуса).</summary>
    Task<ApiResult<BoardResponse>> DeleteStatus(Guid boardId, Guid statusId, Guid moveTo, CancellationToken ct = default);
    Task<ApiResult<BoardResponse>> ReorderStatuses(Guid boardId, ReorderStatusesRequest request, CancellationToken ct = default);

    // Workflow (docs/TZ_workflow_config.md §2)
    Task<ApiResult<WorkflowResponse>> GetWorkflow(Guid boardId, CancellationToken ct = default);
    Task<ApiResult<WorkflowResponse>> SetWorkflow(Guid boardId, SetWorkflowRequest request, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<TaskTransitionResponse>>> GetTransitions(Guid taskId, CancellationToken ct = default);

    // Канбан (docs/TZ_task_views.md §1): без колонки — все колонки первой страницей, с колонкой — её страница с offset.
    Task<ApiResult<TaskBoardResponse>> GetTaskBoard(
        Guid? boardId = null,
        Guid? assigneeId = null,
        bool unassigned = false,
        string? query = null,
        TaskTypeKind? typeKind = null,
        TaskPriority? priority = null,
        string? fql = null,
        Guid? statusId = null,
        StatusType? statusType = null,
        bool other = false,
        int offset = 0,
        int? limit = null,
        CancellationToken ct = default);

    Task<ApiResult<BoardResponse>> SetDoneColumnDays(Guid boardId, int days, CancellationToken ct = default);

    // Спринты и бэклог (docs/TZ_task_views.md §2).
    Task<ApiResult<IReadOnlyList<SprintResponse>>> GetSprints(Guid boardId, CancellationToken ct = default);
    Task<ApiResult<SprintResponse>> CreateSprint(Guid boardId, CreateSprintRequest request, CancellationToken ct = default);
    Task<ApiResult<SprintResponse>> UpdateSprint(Guid sprintId, UpdateSprintRequest request, CancellationToken ct = default);
    Task<ApiResult<SprintResponse>> StartSprint(Guid sprintId, StartSprintRequest request, CancellationToken ct = default);
    Task<ApiResult<SprintResponse>> CompleteSprint(Guid sprintId, CompleteSprintRequest request, CancellationToken ct = default);
    Task<ApiResult<bool>> DeleteSprint(Guid sprintId, CancellationToken ct = default);
    Task<ApiResult<SprintReportResponse>> GetSprintReport(Guid sprintId, CancellationToken ct = default);

    Task<ApiResult<BacklogResponse>> GetBacklog(
        Guid boardId,
        Guid? assigneeId = null,
        bool unassigned = false,
        string? query = null,
        TaskTypeKind? typeKind = null,
        TaskPriority? priority = null,
        string? fql = null,
        Guid? epicId = null,
        CancellationToken ct = default);

    Task<ApiResult<TaskResponse>> SetTaskSprint(Guid taskId, SetTaskSprintRequest request, CancellationToken ct = default);

    // ---- Доступ к проектам ----
    Task<ApiResult<IReadOnlyList<ProjectAccessResponse>>> GetMyAccess(CancellationToken ct = default);
    Task<ApiResult<BoardMembersResponse>> GetBoardMembers(Guid boardId, CancellationToken ct = default);
    Task<ApiResult<BoardMembersResponse>> SetBoardMember(Guid boardId, Guid userId, SetBoardMemberRequest request, CancellationToken ct = default);
    Task<ApiResult<BoardMembersResponse>> RemoveBoardMember(Guid boardId, Guid userId, CancellationToken ct = default);
    Task<ApiResult<BoardMembersResponse>> SetBoardVisibility(Guid boardId, SetVisibilityRequest request, CancellationToken ct = default);
    Task<ApiResult<BoardMembersResponse>> SetBoardDefaultRole(Guid boardId, SetDefaultRoleRequest request, CancellationToken ct = default);

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
        TaskTypeKind? typeKind = null,
        TaskPriority? priority = null,
        Guid? parentId = null,
        string? fql = null,
        CancellationToken ct = default);

    // ---- FQL и сохранённые фильтры (docs/TZ_task_views.md §7); ошибка FQL — ApiResult.FqlError ----
    Task<ApiResult<Flow.Shared.Contracts.Filters.FqlSuggestResponse>> SuggestQuery(string query, int position, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<Flow.Shared.Contracts.Filters.SavedFilterResponse>>> GetFilters(CancellationToken ct = default);
    Task<ApiResult<Flow.Shared.Contracts.Filters.SavedFilterResponse>> GetFilter(Guid id, CancellationToken ct = default);
    Task<ApiResult<Flow.Shared.Contracts.Filters.SavedFilterResponse>> CreateFilter(Flow.Shared.Contracts.Filters.CreateSavedFilterRequest request, CancellationToken ct = default);
    Task<ApiResult<Flow.Shared.Contracts.Filters.SavedFilterResponse>> UpdateFilter(Guid id, Flow.Shared.Contracts.Filters.UpdateSavedFilterRequest request, CancellationToken ct = default);
    Task<ApiResult<bool>> DeleteFilter(Guid id, CancellationToken ct = default);
    Task<ApiResult<Flow.Shared.Contracts.Filters.SavedFilterResponse>> StarFilter(Guid id, bool starred, CancellationToken ct = default);

    Task<ApiResult<TaskResponse>> GetTask(Guid id, CancellationToken ct = default);
    Task<ApiResult<TaskResponse>> CreateTask(Guid boardId, CreateTaskRequest request, CancellationToken ct = default);
    Task<ApiResult<TaskResponse>> UpdateTask(Guid id, UpdateTaskRequest request, CancellationToken ct = default);
    /// <summary>cascade — вместе с подзадачами; без него задача с подзадачами не удаляется (400).</summary>
    Task<ApiResult<bool>> DeleteTask(Guid id, bool cascade = false, CancellationToken ct = default);
    Task<ApiResult<TaskResponse>> AssignTask(Guid id, AssignTaskRequest request, CancellationToken ct = default);
    Task<ApiResult<TaskResponse>> SetDueDate(Guid id, SetTaskDueDateRequest request, CancellationToken ct = default);
    Task<ApiResult<TaskResponse>> SetSchedule(Guid id, SetTaskScheduleRequest request, CancellationToken ct = default);
    Task<ApiResult<TaskResponse>> SetEstimate(Guid id, SetTaskEstimateRequest request, CancellationToken ct = default);
    /// <summary>Родитель в иерархии (docs/TZ_task_model.md §3); null — снять.</summary>
    Task<ApiResult<TaskResponse>> SetParent(Guid id, SetTaskParentRequest request, CancellationToken ct = default);
    /// <summary>Место в ручном порядке проекта (§7): ключ ранга вычисляет сервер по соседям.</summary>
    Task<ApiResult<TaskResponse>> RankTask(Guid id, RankTaskRequest request, CancellationToken ct = default);
    // Связи (docs/TZ_task_model.md §5) и чек-лист (§8)
    Task<ApiResult<IReadOnlyList<TaskLinkResponse>>> GetLinks(Guid taskId, CancellationToken ct = default);
    Task<ApiResult<TaskLinkCreatedResponse>> CreateLink(Guid taskId, CreateTaskLinkRequest request, CancellationToken ct = default);
    Task<ApiResult<bool>> DeleteLink(Guid linkId, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<TaskChecklistItemResponse>>> GetChecklist(Guid taskId, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<TaskChecklistItemResponse>>> AddChecklistItem(Guid taskId, AddChecklistItemRequest request, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<TaskChecklistItemResponse>>> UpdateChecklistItem(Guid taskId, Guid itemId, UpdateChecklistItemRequest request, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<TaskChecklistItemResponse>>> DeleteChecklistItem(Guid taskId, Guid itemId, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<TaskChecklistItemResponse>>> ReorderChecklist(Guid taskId, ReorderChecklistRequest request, CancellationToken ct = default);
    /// <summary>Дерево проекта (docs/TZ_task_views.md §3): фильтры как у списка, maxDepth — глубина от корня обхода.</summary>
    Task<ApiResult<IReadOnlyList<TaskTreeNode>>> GetTree(
        Guid boardId,
        Guid? rootId = null,
        Guid? assigneeId = null,
        bool unassigned = false,
        string? query = null,
        TaskTypeKind? typeKind = null,
        TaskPriority? priority = null,
        Guid? statusId = null,
        string? fql = null,
        int? maxDepth = null,
        CancellationToken ct = default);

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
