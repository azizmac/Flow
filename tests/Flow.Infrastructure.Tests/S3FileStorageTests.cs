using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Flow.Application.Abstractions;
using Flow.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Minio;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Хранилище против настоящего MinIO: подпись запросов, контрольные суммы и path-style адресация
/// у S3-совместимых сервисов расходятся с AWS, и на заглушке этого не увидеть. Требует Docker.
/// </summary>
public sealed class S3FileStorageTests : IAsyncLifetime
{
    private const string Bucket = "flow-test";

    /// <summary>
    /// Образ с quay.io и тот же тег, что в docker-compose.data.yml: из Docker Hub образы MinIO сняты
    /// («pull access denied»), а модуль Testcontainers по умолчанию тянет именно оттуда.
    /// </summary>
    private readonly MinioContainer _container = new MinioBuilder()
        .WithImage("quay.io/minio/minio:RELEASE.2025-04-22T22-12-26Z")
        .Build();

    private IAmazonS3 _client = null!;
    private IFileStorage _storage = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new S3Options
        {
            Endpoint = _container.GetConnectionString(),
            AccessKey = _container.GetAccessKey(),
            SecretKey = _container.GetSecretKey(),
            Bucket = Bucket,
            UseSsl = false
        };

        // Те же настройки, что в AddFlowInfrastructure: без ForcePathStyle MinIO не отвечает,
        // а контрольные суммы AWS v4 он не понимает.
        _client = new AmazonS3Client(options.AccessKey, options.SecretKey, new AmazonS3Config
        {
            ServiceURL = options.Endpoint,
            ForcePathStyle = true,
            UseHttp = true,
            RequestChecksumCalculation = Amazon.Runtime.RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = Amazon.Runtime.ResponseChecksumValidation.WHEN_REQUIRED
        });

        await _client.PutBucketAsync(Bucket);

        _storage = new S3FileStorage(_client, options, NullLogger<S3FileStorage>.Instance);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _container.DisposeAsync();
    }

    private static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));

    private async Task<string> ReadAsync(string key)
    {
        await using var stream = await _storage.OpenReadAsync(key, CancellationToken.None);
        using var reader = new StreamReader(stream!);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task Object_Survives_Put_And_Read()
    {
        await _storage.PutAsync("attachments/board/task/file.txt", Content("содержимое договора"), "text/plain", CancellationToken.None);

        Assert.Equal("содержимое договора", await ReadAsync("attachments/board/task/file.txt"));
    }

    [Fact]
    public async Task Missing_Object_Is_Null_Not_An_Exception()
    {
        // Строка в БД есть, а объекта нет (сбой загрузки, чистка бакета руками) — это 404, а не 500.
        Assert.Null(await _storage.OpenReadAsync("attachments/нет/такого/ключа.bin", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_Is_Idempotent()
    {
        await _storage.PutAsync("удаляемый.txt", Content("файл"), "text/plain", CancellationToken.None);

        await _storage.DeleteAsync("удаляемый.txt", CancellationToken.None);
        await _storage.DeleteAsync("удаляемый.txt", CancellationToken.None);

        Assert.Null(await _storage.OpenReadAsync("удаляемый.txt", CancellationToken.None));
    }

    [Fact]
    public async Task Prefix_Delete_Removes_Whole_Task_And_Keeps_Others()
    {
        await _storage.PutAsync("attachments/b1/t1/a.txt", Content("1"), "text/plain", CancellationToken.None);
        await _storage.PutAsync("attachments/b1/t1/b.txt", Content("2"), "text/plain", CancellationToken.None);
        await _storage.PutAsync("attachments/b1/t2/c.txt", Content("3"), "text/plain", CancellationToken.None);

        await _storage.DeleteByPrefixAsync("attachments/b1/t1/", CancellationToken.None);

        Assert.Null(await _storage.OpenReadAsync("attachments/b1/t1/a.txt", CancellationToken.None));
        Assert.Null(await _storage.OpenReadAsync("attachments/b1/t1/b.txt", CancellationToken.None));
        Assert.Equal("3", await ReadAsync("attachments/b1/t2/c.txt"));
    }

    [Fact]
    public async Task Content_Type_Is_Stored_With_The_Object()
    {
        await _storage.PutAsync("снимок.png", Content("png"), "image/png", CancellationToken.None);

        var metadata = await _client.GetObjectMetadataAsync(Bucket, "снимок.png");

        Assert.Equal("image/png", metadata.Headers.ContentType);
    }
}
