using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;

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
/// Маршруты соответствуют BoardsController / TasksController.
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

    public Task<ApiResult<IReadOnlyList<TaskResponse>>> GetTasks(Guid boardId, CancellationToken ct = default) =>
        Get<IReadOnlyList<TaskResponse>>($"boards/{boardId}/tasks", ct);

    public Task<ApiResult<TaskResponse>> GetTask(Guid id, CancellationToken ct = default) =>
        Get<TaskResponse>($"tasks/{id}", ct);

    public Task<ApiResult<TaskResponse>> CreateTask(Guid boardId, CreateTaskRequest request, CancellationToken ct = default) =>
        Send<TaskResponse>(HttpMethod.Post, $"boards/{boardId}/tasks", request, ct);

    public Task<ApiResult<TaskResponse>> UpdateTask(Guid id, UpdateTaskRequest request, CancellationToken ct = default) =>
        Send<TaskResponse>(HttpMethod.Patch, $"tasks/{id}", request, ct);

    public Task<ApiResult<bool>> DeleteTask(Guid id, CancellationToken ct = default) =>
        Delete($"tasks/{id}", ct);

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
