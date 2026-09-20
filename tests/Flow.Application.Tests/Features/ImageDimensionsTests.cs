using Flow.Application.Features.Attachments;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Размеры картинки из заголовка файла. Заголовки собираются здесь же байтами, а не берутся
/// из файлов-образцов: так видно, что именно проверяется, и тест не зависит от внешних данных.
/// </summary>
public sealed class ImageDimensionsTests
{
    private static byte[] Png(int width, int height, int chunkLength = 13)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        bytes.AddRange(BigEndian(chunkLength));
        bytes.AddRange("IHDR"u8.ToArray());
        bytes.AddRange(BigEndian(width));
        bytes.AddRange(BigEndian(height));
        return [.. bytes];
    }

    private static byte[] Gif(int width, int height) =>
        [.. "GIF89a"u8.ToArray(), (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8)];

    /// <param name="orientation">Тег EXIF Orientation; 0 — сегмента APP1 нет вовсе.</param>
    private static byte[] Jpeg(int width, int height, int orientation = 0, bool withThumbnail = false)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };

        if (orientation > 0)
        {
            var exif = new List<byte>();
            exif.AddRange("Exif"u8.ToArray());
            exif.AddRange([0x00, 0x00]);
            exif.AddRange("II"u8.ToArray());          // little-endian
            exif.AddRange([42, 0]);                   // контрольное значение TIFF
            exif.AddRange([8, 0, 0, 0]);              // смещение IFD0
            exif.AddRange([1, 0]);                    // одна запись
            exif.AddRange([0x12, 0x01]);              // тег Orientation
            exif.AddRange([3, 0]);                    // тип SHORT
            exif.AddRange([1, 0, 0, 0]);              // количество
            exif.AddRange([(byte)orientation, 0, 0, 0]);

            if (withThumbnail)
            {
                // Внутри APP1 лежит настоящий JPEG-миниатюра со своими SOI и SOF. Ровно на ней
                // ломается наивный поиск FF C0 по буферу: он найдёт 160x120 вместо кадра.
                exif.AddRange(Jpeg(160, 120));
            }

            var length = exif.Count + 2;
            bytes.AddRange([0xFF, 0xE1, (byte)(length >> 8), (byte)length]);
            bytes.AddRange(exif);
        }

        // SOF0: длина(2), точность(1), высота(2), ширина(2), число компонент(1).
        bytes.AddRange([0xFF, 0xC0, 0x00, 0x0B, 0x08]);
        bytes.AddRange([(byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03]);
        return [.. bytes];
    }

    private static byte[] BigEndian(int value) =>
        [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    [Fact]
    public void Png_Gif_And_Jpeg_Headers_Are_Read()
    {
        Assert.True(ImageDimensions.TryRead("image/png", Png(700, 393), out var pw, out var ph));
        Assert.Equal((700, 393), (pw, ph));

        Assert.True(ImageDimensions.TryRead("image/gif", Gif(320, 240), out var gw, out var gh));
        Assert.Equal((320, 240), (gw, gh));

        Assert.True(ImageDimensions.TryRead("image/jpeg", Jpeg(800, 600), out var jw, out var jh));
        Assert.Equal((800, 600), (jw, jh));
    }

    /// <summary>
    /// Браузер поворачивает снимок сам (image-orientation: from-image — значение по умолчанию),
    /// поэтому наружу отдаются отображаемые размеры, а не те, что лежат в SOF. Иначе вертикальное
    /// фото с телефона получило бы разметку боком и при нынешнем CSS осталось бы сплющенным.
    /// </summary>
    [Theory]
    [InlineData(1, 4032, 3024)]
    [InlineData(4, 4032, 3024)]
    [InlineData(6, 3024, 4032)]
    [InlineData(8, 3024, 4032)]
    public void Exif_Orientation_Swaps_Sides_For_Quarter_Turns(int orientation, int expectedWidth, int expectedHeight)
    {
        var read = ImageDimensions.TryRead("image/jpeg", Jpeg(4032, 3024, orientation), out var width, out var height);

        Assert.True(read);
        Assert.Equal((expectedWidth, expectedHeight), (width, height));
    }

    /// <summary>Миниатюра внутри EXIF не должна выдаваться за кадр: сегменты обходятся по длине.</summary>
    [Fact]
    public void Exif_Thumbnail_Is_Not_Mistaken_For_The_Frame()
    {
        var read = ImageDimensions.TryRead(
            "image/jpeg", Jpeg(4032, 3024, orientation: 1, withThumbnail: true), out var width, out var height);

        Assert.True(read);
        Assert.Equal((4032, 3024), (width, height));
    }

    /// <summary>
    /// 29 байт с валидной сигнатурой объявляют десять миллиардов пикселей. Каждая сторона по
    /// отдельности правдоподобна — отсекает только потолок площади.
    /// </summary>
    [Fact]
    public void Absurd_Area_Is_Rejected()
    {
        Assert.False(ImageDimensions.TryRead("image/png", Png(100_000, 100_000), out _, out _));
        Assert.False(ImageDimensions.TryRead("image/gif", Gif(65_535, 65_535), out _, out _));
    }

    [Fact]
    public void Broken_Headers_Give_No_Dimensions()
    {
        // Нулевая сторона, чужая длина чанка IHDR, незнакомый тип, пустой буфер.
        Assert.False(ImageDimensions.TryRead("image/png", Png(0, 100), out _, out _));
        Assert.False(ImageDimensions.TryRead("image/png", Png(700, 393, chunkLength: 14), out _, out _));
        Assert.False(ImageDimensions.TryRead("application/pdf", Png(700, 393), out _, out _));
        Assert.False(ImageDimensions.TryRead("image/png", [], out _, out _));
    }

    /// <summary>
    /// Инвариант обрезания: ни одного исключения, а успех обязан давать правдоподобную пару.
    /// Именно так, а не «на обрезанном всегда false»: PNG, обрезанный до 24 байт, размеры знает,
    /// и это его штатное поведение.
    /// </summary>
    [Fact]
    public void Truncation_Never_Throws_And_Never_Lies()
    {
        byte[][] samples = [Png(700, 393), Gif(320, 240), Jpeg(800, 600), Jpeg(4032, 3024, orientation: 6)];
        string[] types = ["image/png", "image/gif", "image/jpeg", "image/webp"];

        foreach (var sample in samples)
        {
            for (var length = 0; length <= sample.Length; length++)
            {
                foreach (var type in types)
                {
                    if (!ImageDimensions.TryRead(type, sample.AsSpan(0, length), out var width, out var height))
                        continue;

                    Assert.InRange(width, 1, 100_000);
                    Assert.InRange(height, 1, 100_000);
                }
            }
        }
    }

    /// <summary>
    /// Буфер из одних 0xFF — вырожденная цепочка маркеров JPEG. Проверяется, что разбор линейный
    /// и завершается: смещение строго растёт, сегмент нулевой длины отвергается.
    /// </summary>
    [Fact]
    public void Marker_Soup_Terminates()
    {
        var soup = new byte[ImageDimensions.HeadLength];
        Array.Fill(soup, (byte)0xFF);
        soup[0] = 0xFF;
        soup[1] = 0xD8;

        Assert.False(ImageDimensions.TryRead("image/jpeg", soup, out _, out _));
    }
}
