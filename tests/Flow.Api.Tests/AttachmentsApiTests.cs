using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Flow.Shared.Contracts.Attachments;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// HTTP-поверхность вложений: multipart-загрузка, заголовки скачивания и коды ответов.
/// Хранилище в фикстуре — в памяти (см. ApiFixture), поэтому тесты не зависят от MinIO.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AttachmentsApiTests(ApiFixture api)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9, 9, 9];

    private static int _keySuffix;

    private static async Task<TaskResponse> CreateTaskAsync(HttpClient client)
    {
        var key = $"ATT{Interlocked.Increment(ref _keySuffix)}";
        using var board = await client.PostAsJsonAsync("/boards", new CreateBoardRequest($"Вложения {key}", key));
        var created = (await board.Content.ReadFromJsonAsync<BoardResponse>())!;

        using var task = await client.PostAsJsonAsync($"/boards/{created.Id}/tasks", new CreateTaskRequest("Задача", null, null));
        return (await task.Content.ReadFromJsonAsync<TaskResponse>())!;
    }

    private static MultipartFormDataContent File(byte[] bytes, string fileName)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid taskId, byte[] bytes, string fileName) =>
        client.PostAsync($"/tasks/{taskId}/attachments", File(bytes, fileName));

    [Fact]
    public async Task Upload_Requires_Token()
    {
        using var anonymous = api.CreateClient();
        using var response = await UploadAsync(anonymous, Guid.NewGuid(), [1, 2, 3], "файл.txt");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Returns_Created_With_Location()
    {
        using var client = api.CreateClientAs();
        var task = await CreateTaskAsync(client);

        using var response = await UploadAsync(client, task.Id, Encoding.UTF8.GetBytes("смета"), "смета.xlsx");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var attachment = (await response.Content.ReadFromJsonAsync<AttachmentResponse>())!;
        Assert.Equal("смета.xlsx", attachment.FileName);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task Download_Comes_Back_Byte_For_Byte_With_Safe_Headers()
    {
        using var client = api.CreateClientAs();
        var task = await CreateTaskAsync(client);
        var bytes = Encoding.UTF8.GetBytes("содержимое договора");

        using var uploaded = await UploadAsync(client, task.Id, bytes, "договор поставки.txt");
        var attachment = (await uploaded.Content.ReadFromJsonAsync<AttachmentResponse>())!;

        using var download = await client.GetAsync($"/attachments/{attachment.Id}/content");

        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        // Имя с пробелами и кириллицей едет параметром filename* (RFC 5987) и доезжает целым.
        Assert.Contains("filename*=", download.Content.Headers.ContentDisposition.ToString());
        Assert.Equal("договор поставки.txt", download.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Contains("nosniff", download.Headers.GetValues("X-Content-Type-Options"));
    }

    [Fact]
    public async Task Inline_Works_For_Images_Only()
    {
        using var client = api.CreateClientAs();
        var task = await CreateTaskAsync(client);

        using var image = await UploadAsync(client, task.Id, Png, "снимок.png");
        var png = (await image.Content.ReadFromJsonAsync<AttachmentResponse>())!;

        using var document = await UploadAsync(client, task.Id, Encoding.UTF8.GetBytes("<html/>"), "страница.html");
        var html = (await document.Content.ReadFromJsonAsync<AttachmentResponse>())!;

        using var inlineImage = await client.GetAsync($"/attachments/{png.Id}/content?inline=true");
        using var inlineHtml = await client.GetAsync($"/attachments/{html.Id}/content?inline=true");

        Assert.Equal("inline", inlineImage.Content.Headers.ContentDisposition!.DispositionType);
        // HTML с нашего origin показывать нельзя ни при каких параметрах запроса.
        Assert.Equal("attachment", inlineHtml.Content.Headers.ContentDisposition!.DispositionType);
    }

    /// <summary>
    /// Отказы хендлера в кодах HTTP. Сами правила — лимиты, дубли, запрещённые расширения — проверяются
    /// на фейках в Flow.Application.Tests; здесь важно только то, чего там нет: во что они превращаются
    /// на проводе.
    /// </summary>
    [Fact]
    public async Task Rejections_Map_To_Status_Codes()
    {
        using var client = api.CreateClientAs();
        var task = await CreateTaskAsync(client);
        var bytes = Encoding.UTF8.GetBytes("одинаковое содержимое");

        using var first = await UploadAsync(client, task.Id, bytes, "первый.txt");
        using var duplicate = await UploadAsync(client, task.Id, bytes, "второй.txt");
        using var blocked = await UploadAsync(client, task.Id, [1, 2, 3], "установщик.exe");
        using var empty = await UploadAsync(client, task.Id, [], "пустой.txt");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Reader_Cannot_Upload_But_Can_Download()
    {
        using var owner = api.CreateClientAs();
        var task = await CreateTaskAsync(owner);
        using var uploaded = await UploadAsync(owner, task.Id, Encoding.UTF8.GetBytes("для чтения"), "инструкция.txt");
        var attachment = (await uploaded.Content.ReadFromJsonAsync<AttachmentResponse>())!;

        using var created = await owner.PostAsJsonAsync(
            "/users",
            new CreateUserRequest("att.reader", "att.reader@example.com", "A", "B", "correct horse battery", UserRole.Reader));
        var reader = (await created.Content.ReadFromJsonAsync<UserResponse>())!;

        using var client = api.CreateClientAs(reader.Id);
        using var upload = await UploadAsync(client, task.Id, [1, 2, 3], "нельзя.txt");
        using var download = await client.GetAsync($"/attachments/{attachment.Id}/content");

        Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Fact]
    public async Task List_And_Delete()
    {
        using var client = api.CreateClientAs();
        var task = await CreateTaskAsync(client);
        using var uploaded = await UploadAsync(client, task.Id, Encoding.UTF8.GetBytes("файл"), "файл.txt");
        var attachment = (await uploaded.Content.ReadFromJsonAsync<AttachmentResponse>())!;

        var list = await client.GetFromJsonAsync<IReadOnlyList<AttachmentResponse>>($"/tasks/{task.Id}/attachments");
        Assert.Single(list!);

        using var deleted = await client.DeleteAsync($"/attachments/{attachment.Id}");
        using var afterDelete = await client.GetAsync($"/attachments/{attachment.Id}/content");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Attachments_Of_Unknown_Task_Are_Not_Found()
    {
        using var client = api.CreateClientAs();

        using var response = await client.GetAsync($"/tasks/{Guid.NewGuid()}/attachments");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
