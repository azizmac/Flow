using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flow.Application.Abstractions;
using Flow.Application.Features.Search;
using Flow.Infrastructure.Search.Extraction;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Presentation = DocumentFormat.OpenXml.Presentation;
using Word = DocumentFormat.OpenXml.Wordprocessing;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Извлечение текста из вложений. Файлы собираются здесь же, в памяти, — тест проверяет реальные
/// форматы, а не заглушки: в PDF текст лежит командами рисования, а в xlsx строки живут отдельной
/// таблицей, и именно на этом ошибаются.
/// </summary>
public class TextExtractionTests
{
    private static ITextExtractor Create(int maxChars = 200_000) =>
        new CompositeTextExtractor(
            [new PlainTextExtractor(), new PdfTextExtractor(), new OpenXmlTextExtractor()],
            new SearchOptions { Indexing = new SearchIndexingOptions { MaxDocumentChars = maxChars } },
            NullLogger<CompositeTextExtractor>.Instance);

    private static Task<string> Extract(ITextExtractor extractor, byte[] bytes, string fileName, string? contentType = null) =>
        extractor.ExtractAsync(new MemoryStream(bytes), fileName, contentType, CancellationToken.None);

    [Fact]
    public async Task Plain_Text_Is_Read_As_Is()
    {
        var extractor = Create();
        var bytes = Encoding.UTF8.GetBytes("# Регламент\n\nПриёмка груза на складе.");

        var text = await Extract(extractor, bytes, "регламент.md");

        Assert.Contains("Приёмка груза на складе.", text);
        Assert.True(extractor.CanExtract("заметка.txt", null));
        Assert.True(extractor.CanExtract("без-расширения", "text/plain"));
    }

    [Fact]
    public async Task Utf8_Bom_Does_Not_Leak_Into_Text()
    {
        var extractor = Create();
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Договор поставки")).ToArray();

        var text = await Extract(extractor, bytes, "договор.txt");

        // BOM в начале текста испортил бы и подсветку, и tsvector.
        Assert.Equal("Договор поставки", text);
    }

    [Fact]
    public async Task Pdf_Text_Layer_Is_Extracted()
    {
        var extractor = Create();

        var text = await Extract(extractor, BuildPdf("Akt priemki gruza", "Storony podpisali dokument"), "akt.pdf");

        Assert.Contains("Akt priemki gruza", text);
        Assert.Contains("Storony podpisali dokument", text);
    }

    [Fact]
    public async Task Docx_Keeps_Paragraph_Boundaries()
    {
        var extractor = Create();

        var text = await Extract(extractor, BuildDocx("Техническое задание", "Сроки и порядок приёмки"), "тз.docx");

        // Абзацы должны остаться разными строками: чанкер режет именно по ним.
        Assert.Equal("Техническое задание\nСроки и порядок приёмки", text);
    }

    [Fact]
    public async Task Xlsx_Resolves_Shared_Strings()
    {
        var extractor = Create();

        var text = await Extract(extractor, BuildXlsx("Наименование", "Ботинки кожаные"), "прайс.xlsx");

        // В ячейке лежит индекс в SharedStringTable: без его разбора вернулись бы числа 0 и 1.
        Assert.Contains("Наименование", text);
        Assert.Contains("Ботинки кожаные", text);
        Assert.DoesNotContain("0 1", text);
    }

    [Fact]
    public async Task Pptx_Collects_Slide_Text()
    {
        var extractor = Create();

        var text = await Extract(extractor, BuildPptx("Итоги квартала"), "презентация.pptx");

        Assert.Contains("Итоги квартала", text);
    }

    [Fact]
    public async Task Unsupported_Format_Is_Empty_Not_An_Error()
    {
        var extractor = Create();

        Assert.False(extractor.CanExtract("схема.dwg", null));
        Assert.Equal(string.Empty, await Extract(extractor, [1, 2, 3], "схема.dwg"));
    }

    [Fact]
    public async Task Broken_File_Does_Not_Throw()
    {
        var extractor = Create();
        var garbage = Encoding.UTF8.GetBytes("это совсем не pdf");

        // Одно испорченное вложение не должно останавливать индексацию остальных.
        Assert.Equal(string.Empty, await Extract(extractor, garbage, "битый.pdf"));
    }

    [Fact]
    public async Task Long_Text_Is_Cut_To_The_Limit()
    {
        var extractor = Create(maxChars: 1000);
        var bytes = Encoding.UTF8.GetBytes(new string('я', 5000));

        var text = await Extract(extractor, bytes, "простыня.txt");

        Assert.Equal(1000, text.Length);
    }

    // ---- сборка файлов ----

    private static byte[] BuildPdf(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(595, 842);

        var y = 780;
        foreach (var line in lines)
        {
            page.AddText(line, 12, new UglyToad.PdfPig.Core.PdfPoint(50, y), font);
            y -= 24;
        }

        return builder.Build();
    }

    private static byte[] BuildDocx(params string[] paragraphs)
    {
        using var buffer = new MemoryStream();
        using (var document = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document))
        {
            var body = document.AddMainDocumentPart().Document = new Word.Document(new Word.Body());
            foreach (var paragraph in paragraphs)
                body.Body!.AppendChild(new Word.Paragraph(new Word.Run(new Word.Text(paragraph))));
        }

        return buffer.ToArray();
    }

    private static byte[] BuildXlsx(params string[] values)
    {
        using var buffer = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var sharedPart = workbookPart.AddNewPart<SharedStringTablePart>();
            sharedPart.SharedStringTable = new SharedStringTable();
            foreach (var value in values)
                sharedPart.SharedStringTable.AppendChild(new SharedStringItem(new Text(value)));

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            for (var i = 0; i < values.Length; i++)
            {
                data.AppendChild(new Row(new Cell
                {
                    DataType = CellValues.SharedString,
                    CellValue = new CellValue(i.ToString())
                }));
            }

            worksheetPart.Worksheet = new Worksheet(data);

            workbookPart.Workbook.AppendChild(new Sheets(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Лист1"
            }));
        }

        return buffer.ToArray();
    }

    private static byte[] BuildPptx(string title)
    {
        using var buffer = new MemoryStream();
        using (var document = PresentationDocument.Create(buffer, PresentationDocumentType.Presentation))
        {
            var presentationPart = document.AddPresentationPart();
            presentationPart.Presentation = new Presentation.Presentation();

            var slidePart = presentationPart.AddNewPart<SlidePart>();
            slidePart.Slide = new Presentation.Slide(
                new Presentation.CommonSlideData(
                    new Presentation.ShapeTree(
                        new Presentation.NonVisualGroupShapeProperties(
                            new Presentation.NonVisualDrawingProperties { Id = 1, Name = "" },
                            new Presentation.NonVisualGroupShapeDrawingProperties(),
                            new Presentation.ApplicationNonVisualDrawingProperties()),
                        new Presentation.GroupShapeProperties(),
                        new Presentation.Shape(
                            new Presentation.NonVisualShapeProperties(
                                new Presentation.NonVisualDrawingProperties { Id = 2, Name = "Заголовок" },
                                new Presentation.NonVisualShapeDrawingProperties(),
                                new Presentation.ApplicationNonVisualDrawingProperties()),
                            new Presentation.ShapeProperties(),
                            new Presentation.TextBody(
                                new Drawing.BodyProperties(),
                                new Drawing.Paragraph(new Drawing.Run(new Drawing.Text(title))))))));

            presentationPart.Presentation.AppendChild(new Presentation.SlideIdList(new Presentation.SlideId
            {
                Id = 256U,
                RelationshipId = presentationPart.GetIdOfPart(slidePart)
            }));
        }

        return buffer.ToArray();
    }
}
