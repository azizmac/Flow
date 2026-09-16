using System.Text;
using Flow.Application.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Flow.Infrastructure.Search.Extraction;

/// <summary>
/// PDF через PdfPig. Текстовый слой берётся в порядке чтения (ContentOrderTextExtractor), а не
/// в порядке команд рисования: иначе колонки и таблицы склеиваются в кашу и вектор получается
/// ни о чём. Скан без текстового слоя даёт пустую строку — распознавание это этап 8, не этот.
/// </summary>
internal sealed class PdfTextExtractor : ITextExtractor
{
    public bool CanExtract(string fileName, string? contentType) =>
        string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<string> ExtractAsync(Stream content, string fileName, string? contentType, CancellationToken cancellationToken)
    {
        // PdfPig читает документ вразнобой по смещениям — поток обязан быть перематываемым.
        var seekable = await Seekable.EnsureAsync(content, cancellationToken);

        using var document = PdfDocument.Open(seekable);
        var text = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pageText = ContentOrderTextExtractor.GetText(page);
            if (!string.IsNullOrWhiteSpace(pageText))
                text.Append(pageText.Trim()).Append("\n\n");
        }

        return text.ToString().TrimEnd();
    }
}
