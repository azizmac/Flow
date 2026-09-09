using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Contracts.Users;

namespace Flow.Client.Services;

/// <summary>Результат вызова API: либо значение, либо человекочитаемая ошибка (тело { message } от 400/409).</summary>
public sealed record ApiResult<T>(T? Value, string? Error, HttpStatusCode Status)
{
    public bool Ok => Error is null;
    public bool NotFound => Status == HttpStatusCode.NotFound;
    public bool Conflict => Status == HttpStatusCode.Conflict;

    public static ApiResult<T> Success(T value, HttpStatusCode status) => new(value, null, status);
    public static ApiResult<T> Fail(string error, HttpStatusCode status) => new(default, error, status);
}

/// <summary>
/// Тонкий типизированный клиент Flow.Api поверх HttpClient. DTO — из Flow.Shared, ничего не дублируется.
/// Маршруты соответствуют BoardsController / TasksController / UsersController.
/// </summary>
public sealed class FlowApi(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ApiResult<IReadOnlyList<BoardResponse>>> GetBoards(CancellationToken ct = default) =>
        Get<IReadOnlyList<BoardResponse>>("boards", ct);

    public Task<ApiResult<BoardResponse>> GetBoard(Guid id, CancellationToken ct = default) =>
        Get<BoardResponse>($"boards/{id}", ct);

    public Task<ApiResult<BoardResponse>> CreateBoard(CreateBoardRequest request, CancellationToken ct = default) =>
        Send<BoardResponse>(HttpMethod.Post, "boards", request, ct);

    public Task<ApiResult<BoardResponse>> RenameBoard(Guid id, RenameBoardRequest request, CancellationToken ct = default) =>
        Send<BoardResponse>(HttpMethod.Patch, $"boards/{id}/name", request, ct);

    public Task<ApiResult<bool>> DeleteBoard(Guid id, CancellationToken ct = default) =>
        Delete($"boards/{id}", ct);

    public Task<ApiResult<IReadOnlyList<TaskResponse>>> GetTasks(Guid boardId, Guid? assigneeId = null, CancellationToken ct = default) =>
        Get<IReadOnlyList<TaskResponse>>(assigneeId is null ? $"boards/{boardId}/tasks" : $"boards/{boardId}/tasks?assigneeId={assigneeId}", ct);

    public Task<ApiResult<TaskResponse>> GetTask(Guid id, CancellationToken ct = default) =>
        Get<TaskResponse>($"tasks/{id}", ct);

    public Task<ApiResult<TaskResponse>> CreateTask(Guid boardId, CreateTaskRequest request, CancellationToken ct = default) =>
        Send<TaskResponse>(HttpMethod.Post, $"boards/{boardId}/tasks", request, ct);

    public Task<ApiResult<TaskResponse>> UpdateTask(Guid id, UpdateTaskRequest request, CancellationToken ct = default) =>
        Send<TaskResponse>(HttpMethod.Patch, $"tasks/{id}", request, ct);

    public Task<ApiResult<bool>> DeleteTask(Guid id, CancellationToken ct = default) =>
        Delete($"tasks/{id}", ct);

    /// <summary>UserId = null — снять исполнителя. 400 — пользователь деактивирован.</summary>
    public Task<ApiResult<TaskResponse>> AssignTask(Guid id, AssignTaskRequest request, CancellationToken ct = default) =>
        Send<TaskResponse>(HttpMethod.Patch, $"tasks/{id}/assignee", request, ct);

    // ---- пользователи (UsersController) ----

    public Task<ApiResult<IReadOnlyList<UserResponse>>> GetUsers(bool includeInactive = false, CancellationToken ct = default) =>
        Get<IReadOnlyList<UserResponse>>(includeInactive ? "users?includeInactive=true" : "users", ct);

    /// <summary>Автодополнение для @: только активные, ILIKE по username/имени/фамилии.</summary>
    public Task<ApiResult<IReadOnlyList<UserResponse>>> SearchUsers(string query, int limit = 10, CancellationToken ct = default) =>
        Get<IReadOnlyList<UserResponse>>($"users/search?q={Uri.EscapeDataString(query)}&limit={limit}", ct);

    public Task<ApiResult<UserResponse>> GetUser(Guid id, CancellationToken ct = default) =>
        Get<UserResponse>($"users/{id}", ct);

    public Task<ApiResult<UserResponse>> GetUserByUsername(string username, CancellationToken ct = default) =>
        Get<UserResponse>($"users/by-username/{Uri.EscapeDataString(username)}", ct);

    /// <summary>409 (Conflict) — username или email заняты; текст — в Error.</summary>
    public Task<ApiResult<UserResponse>> CreateUser(CreateUserRequest request, CancellationToken ct = default) =>
        Send<UserResponse>(HttpMethod.Post, "users", request, ct);

    public Task<ApiResult<UserResponse>> UpdateUserProfile(Guid id, UpdateUserProfileRequest request, CancellationToken ct = default) =>
        Send<UserResponse>(HttpMethod.Patch, $"users/{id}", request, ct);

    public Task<ApiResult<UserResponse>> ChangeUsername(Guid id, ChangeUsernameRequest request, CancellationToken ct = default) =>
        Send<UserResponse>(HttpMethod.Patch, $"users/{id}/username", request, ct);

    public Task<ApiResult<UserResponse>> ChangeEmail(Guid id, ChangeEmailRequest request, CancellationToken ct = default) =>
        Send<UserResponse>(HttpMethod.Patch, $"users/{id}/email", request, ct);

    /// <summary>PUT: добавляет ссылку или заменяет URL ссылки того же типа.</summary>
    public Task<ApiResult<UserResponse>> SetUserLink(Guid id, UserLinkType type, SetUserLinkRequest request, CancellationToken ct = default) =>
        Send<UserResponse>(HttpMethod.Put, $"users/{id}/links/{type}", request, ct);

    public Task<ApiResult<bool>> RemoveUserLink(Guid id, UserLinkType type, CancellationToken ct = default) =>
        Delete($"users/{id}/links/{type}", ct);

    /// <summary>204 — деактивирован; 400 — уже неактивен.</summary>
    public Task<ApiResult<bool>> DeactivateUser(Guid id, CancellationToken ct = default) =>
        SendNoContent($"users/{id}/deactivate", ct);

    public Task<ApiResult<bool>> ActivateUser(Guid id, CancellationToken ct = default) =>
        SendNoContent($"users/{id}/activate", ct);

    private async Task<ApiResult<T>> Get<T>(string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, ct);
            return await Read<T>(response, ct);
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<T>.Fail(NetworkError(ex), 0);
        }
    }

    private async Task<ApiResult<T>> Send<T>(HttpMethod method, string url, object body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, options: Json) };
            using var response = await http.SendAsync(request, ct);
            return await Read<T>(response, ct);
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<T>.Fail(NetworkError(ex), 0);
        }
    }

    /// <summary>POST без тела и без ответа (deactivate/activate): true — успех, false — 404.</summary>
    private async Task<ApiResult<bool>> SendNoContent(string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsync(url, null, ct);
            if (response.IsSuccessStatusCode)
                return ApiResult<bool>.Success(true, response.StatusCode);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return ApiResult<bool>.Success(false, response.StatusCode);
            return ApiResult<bool>.Fail(await ReadError(response, ct), response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<bool>.Fail(NetworkError(ex), 0);
        }
    }

    private async Task<ApiResult<bool>> Delete(string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.DeleteAsync(url, ct);
            if (response.IsSuccessStatusCode)
                return ApiResult<bool>.Success(true, response.StatusCode);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return ApiResult<bool>.Success(false, response.StatusCode);
            return ApiResult<bool>.Fail(await ReadError(response, ct), response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<bool>.Fail(NetworkError(ex), 0);
        }
    }

    private static async Task<ApiResult<T>> Read<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            return ApiResult<T>.Fail(await ReadError(response, ct), response.StatusCode);

        var value = await response.Content.ReadFromJsonAsync<T>(Json, ct);
        return value is null
            ? ApiResult<T>.Fail("Пустой ответ сервера.", response.StatusCode)
            : ApiResult<T>.Success(value, response.StatusCode);
    }

    private static async Task<string> ReadError(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
            return "Не найдено.";

        try
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(text))
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Name.Equals("message", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                            return prop.Value.GetString()!;
                        // ASP.NET ValidationProblemDetails: { errors: { Field: ["..."] } }
                        if (prop.Name.Equals("errors", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var field in prop.Value.EnumerateObject())
                                if (field.Value.ValueKind == JsonValueKind.Array && field.Value.GetArrayLength() > 0)
                                    return field.Value[0].GetString() ?? "Ошибка запроса.";
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // не JSON — ниже вернём общий текст
        }

        return $"Ошибка сервера ({(int)response.StatusCode}).";
    }

    private static string NetworkError(HttpRequestException ex) =>
        $"Нет связи с API ({ex.Message}). Проверьте, что Flow.Api запущен и CORS настроен.";
}
