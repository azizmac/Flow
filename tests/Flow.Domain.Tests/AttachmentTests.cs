using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

public class AttachmentTests
{
    private static readonly byte[] Hash = new byte[32];

    private static Attachment Create(string fileName = "макет.pdf", long size = 1024) =>
        Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), fileName, "application/pdf", size, Hash, Guid.NewGuid());

    [Fact]
    public void Create_Should_BuildKeyFromIdentifiers()
    {
        var taskId = Guid.NewGuid();
        var boardId = Guid.NewGuid();

        var attachment = Attachment.Create(taskId, boardId, "Отчёт за квартал.pdf", "application/pdf", 10, Hash, Guid.NewGuid());

        // Имя файла в ключ не попадает: кириллица, пробелы и «../» сделали бы ключ ненадёжным.
        Assert.Equal($"attachments/{boardId}/{taskId}/{attachment.Id}.pdf", attachment.StorageKey);
        Assert.Equal("Отчёт за квартал.pdf", attachment.FileName);
    }

    [Fact]
    public void Create_Should_StripPathFromFileName()
    {
        var attachment = Create(@"C:\Users\ilya\Desktop\смета.xlsx");

        Assert.Equal("смета.xlsx", attachment.FileName);
        Assert.EndsWith(".xlsx", attachment.StorageKey);
    }

    [Fact]
    public void Create_Should_DropControlCharacters()
    {
        var attachment = Create("плохое\u0000имя\u001F.txt");

        Assert.Equal("плохоеимя.txt", attachment.FileName);
    }

    [Fact]
    public void Create_Should_TrimLongFileName()
    {
        var attachment = Create(new string('и', 400) + ".txt");

        Assert.Equal(Attachment.FileNameMaxLength, attachment.FileName.Length);
    }

    [Fact]
    public void Create_Should_SkipSuspiciousExtensionInKey()
    {
        // Расширение из полусотни символов или со спецсимволами в ключ не пойдёт — файл всё равно сохранится.
        var attachment = Create("архив.tar gz");

        Assert.EndsWith(attachment.Id.ToString(), attachment.StorageKey);
    }

    [Fact]
    public void Create_Should_FallBackToOctetStream_WhenContentTypeIsEmpty()
    {
        var attachment = Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), "файл.bin", "  ", 10, Hash, Guid.NewGuid());

        Assert.Equal("application/octet-stream", attachment.ContentType);
    }

    [Fact]
    public void Create_Should_Throw_ForEmptyFileName() =>
        Assert.Throws<ArgumentException>(() => Create("   "));

    [Fact]
    public void Create_Should_Throw_ForEmptyFile() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(size: 0));

    [Fact]
    public void Create_Should_Throw_ForWrongHashLength() =>
        Assert.Throws<ArgumentException>(() =>
            Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), "файл.txt", "text/plain", 10, [1, 2, 3], Guid.NewGuid()));

    [Fact]
    public void Create_Should_Throw_ForEmptyIdentifiers()
    {
        Assert.Throws<ArgumentException>(() =>
            Attachment.Create(Guid.Empty, Guid.NewGuid(), "файл.txt", "text/plain", 10, Hash, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() =>
            Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), "файл.txt", "text/plain", 10, Hash, Guid.Empty));
    }
}
