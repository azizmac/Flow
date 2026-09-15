using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flow.Application.Abstractions;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace Flow.Infrastructure.Search.Extraction;

/// <summary>
/// Документы Office: .docx, .xlsx, .pptx. Старые бинарные .doc/.xls не поддерживаются — это другой
/// формат и другая библиотека; такие файлы просто не индексируются.
/// </summary>
internal sealed class OpenXmlTextExtractor : ITextExtractor
{
    private static readonly string[] Extensions = [".docx", ".xlsx", ".pptx"];

    public bool CanExtract(string fileName, string? contentType) =>
        Extensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    public async Task<string> ExtractAsync(Stream content, string fileName, string? contentType, CancellationToken cancellationToken)
    {
        var seekable = await Seekable.EnsureAsync(content, cancellationToken);

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".docx" => FromWord(seekable),
            ".xlsx" => FromSpreadsheet(seekable),
            ".pptx" => FromPresentation(seekable),
            _ => string.Empty
        };
    }

    /// <summary>Абзацы, а не InnerText всего документа: иначе текст слипается в одну строку без границ.</summary>
    private static string FromWord(Stream stream)
    {
        using var document = WordprocessingDocument.Open(stream, isEditable: false);

        var body = document.MainDocumentPart?.Document.Body;
        if (body is null)
            return string.Empty;

        var lines = body.Descendants<Word.Paragraph>()
            .Select(paragraph => paragraph.InnerText.Trim())
            .Where(line => line.Length > 0);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Ячейки таблицы: строка листа — строка текста. Главная особенность формата — общие строки:
    /// в самой ячейке лежит не текст, а индекс в SharedStringTable, и InnerText вернул бы число.
    /// </summary>
    private static string FromSpreadsheet(Stream stream)
    {
        using var document = SpreadsheetDocument.Open(stream, isEditable: false);

        var workbook = document.WorkbookPart;
        if (workbook is null)
            return string.Empty;

        var shared = workbook.SharedStringTablePart?.SharedStringTable;
        var text = new StringBuilder();

        foreach (var sheet in workbook.WorksheetParts)
        {
            foreach (var row in sheet.Worksheet.Descendants<Row>())
            {
                var values = row.Elements<Cell>()
                    .Select(cell => CellText(cell, shared))
                    .Where(value => value.Length > 0);

                var line = string.Join(" ", values);
                if (line.Length > 0)
                    text.Append(line).Append('\n');
            }
        }

        return text.ToString().TrimEnd();
    }

    private static string CellText(Cell cell, SharedStringTable? shared)
    {
        var value = cell.CellValue?.Text ?? cell.InnerText;

        if (cell.DataType?.Value == CellValues.SharedString
            && shared is not null
            && int.TryParse(value, out var index)
            && index >= 0 && index < shared.ChildElements.Count)
        {
            return shared.ChildElements[index].InnerText.Trim();
        }

        return value.Trim();
    }

    /// <summary>Слайды: текстовые прогоны Drawing.Text — они есть и в заголовках, и в таблицах, и в заметках.</summary>
    private static string FromPresentation(Stream stream)
    {
        using var document = PresentationDocument.Open(stream, isEditable: false);

        var slides = document.PresentationPart?.SlideParts;
        if (slides is null)
            return string.Empty;

        var text = new StringBuilder();

        foreach (var slide in slides)
        {
            var words = slide.Slide.Descendants<Drawing.Text>()
                .Select(run => run.Text.Trim())
                .Where(word => word.Length > 0);

            var line = string.Join(" ", words);
            if (line.Length > 0)
                text.Append(line).Append("\n\n");
        }

        return text.ToString().TrimEnd();
    }
}
