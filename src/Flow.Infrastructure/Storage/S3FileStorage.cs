using Amazon.S3;
using Amazon.S3.Model;
using Flow.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.Storage;

/// <summary>
/// Объектное хранилище поверх S3-совместимого сервиса (в стеке данных это MinIO). Клиент — AWSSDK,
/// а не родной клиент MinIO: замена хранилища уже обсуждается (docs/TZ_infra_data_split.md),
/// и привязываться к конкретной реализации незачем.
/// </summary>
internal sealed class S3FileStorage(IAmazonS3 client, S3Options options, ILogger<S3FileStorage> logger) : IFileStorage
{
    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        await client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = options.Bucket,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                // Поток закрывает вызывающая сторона: у неё он ещё может понадобиться.
                // DisablePayloadSigning здесь нельзя — AWS SDK требует для него HTTPS, а внутри
                // compose хранилище работает по http.
                AutoCloseStream = false
            },
            cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetObjectAsync(options.Bucket, key, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Объекта нет: строка в БД есть, а файл пропал (сбой загрузки, чистка бакета руками).
            logger.LogWarning("Объект {Key} не найден в бакете {Bucket}.", key, options.Bucket);
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        // S3 считает удаление несуществующего ключа успехом — повторный вызов безопасен.
        client.DeleteObjectAsync(options.Bucket, key, cancellationToken);

    /// <summary>
    /// Ключи вложений начинаются с проекта и задачи, поэтому каскад сводится к удалению префикса.
    /// Листинг постраничный: за раз S3 отдаёт не больше 1000 ключей.
    /// </summary>
    public async Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        string? continuation = null;

        do
        {
            var listed = await client.ListObjectsV2Async(
                new ListObjectsV2Request { BucketName = options.Bucket, Prefix = prefix, ContinuationToken = continuation },
                cancellationToken);

            if (listed.S3Objects is { Count: > 0 })
            {
                await client.DeleteObjectsAsync(
                    new DeleteObjectsRequest
                    {
                        BucketName = options.Bucket,
                        Objects = listed.S3Objects.Select(item => new KeyVersion { Key = item.Key }).ToList()
                    },
                    cancellationToken);
            }

            continuation = listed.IsTruncated == true ? listed.NextContinuationToken : null;
        }
        while (continuation is not null);
    }
}
