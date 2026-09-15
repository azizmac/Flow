namespace Flow.Infrastructure.Search.Extraction;

/// <summary>
/// PdfPig и OpenXml читают файл вразнобой по смещениям и требуют перематываемый поток, а из S3
/// или из HTTP приходит поток «только вперёд». Здесь он при необходимости буферизуется в память —
/// размер вложения ограничен до вызова извлекателя (см. Search:Indexing:MaxDocumentChars и лимит
/// загрузки в будущем ТЗ вложений).
/// </summary>
internal static class Seekable
{
    public static async Task<Stream> EnsureAsync(Stream content, CancellationToken cancellationToken)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
            return content;
        }

        var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }
}
