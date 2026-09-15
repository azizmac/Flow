using System.Text;
using Flow.Application.Abstractions;

namespace Flow.Infrastructure.Search.Extraction;

/// <summary>Простой текст и Markdown: читаются как есть, разметка остаётся частью текста.</summary>
internal sealed class PlainTextExtractor : ITextExtractor
{
    private static readonly string[] Extensions = [".txt", ".md", ".markdown", ".log", ".csv"];

    public bool CanExtract(string fileName, string? contentType) =>
        Extensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase)
        || contentType is not null && contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);

    public async Task<string> ExtractAsync(Stream content, string fileName, string? contentType, CancellationToken cancellationToken)
    {
        // detectEncodingFromByteOrderMarks: файлы из Windows часто приходят с BOM и в UTF-16.
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
