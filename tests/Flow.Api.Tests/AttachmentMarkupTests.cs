using Flow.Client.Markdown;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Разметка картинки-вложения. Тест живёт здесь, а не в Flow.Client: своего тестового проекта
/// у интерфейса нет, а Flow.Api.Tests и так ссылается на него через хост. Фикстура с Postgres
/// не нужна — ToHtml чистая функция, поэтому класс намеренно вне ApiCollection.
/// </summary>
public sealed class AttachmentMarkupTests
{
    private static readonly Guid Id = Guid.Parse("3f2a9c14-0000-0000-0000-000000000001");

    private static string Render(string text, (int Width, int Height)? size) =>
        FlowMarkdown.ToHtml(text, _ => size);

    /// <summary>
    /// Пара width/height плюс потолок ширины: без него max-height режет высоту, не пересчитывая
    /// ширину, и картинка растягивается. Потолок считается по контентной коробке (box-sizing: border-box),
    /// поэтому от 420 вычитается рамка и прибавляется обратно: 418 * 800/600 + 2 = 559.33.
    /// </summary>
    [Fact]
    public void Image_With_Known_Size_Gets_Attributes_And_Width_Cap()
    {
        var html = Render($"![снимок](attachment:{Id})", (800, 600));

        Assert.Contains("width=\"800\"", html);
        Assert.Contains("height=\"600\"", html);
        Assert.Contains("max-width:min(100%,559.33px)", html);
        Assert.Contains("loading=\"lazy\"", html);
    }

    /// <summary>Размеров нет — поведение ровно как до этой работы: никаких атрибутов и никакого стиля.</summary>
    [Fact]
    public void Image_Without_Known_Size_Stays_As_Before()
    {
        var html = Render($"![снимок](attachment:{Id})", null);

        Assert.DoesNotContain("width=", html);
        Assert.DoesNotContain("height=", html);
        Assert.DoesNotContain("max-width", html);
        Assert.Contains($"/files/{Id}", html);
    }

    /// <summary>
    /// Автор задал размер сам. При дубле парсер HTML оставляет первый атрибут и выбрасывает второй,
    /// так что наша ширина пропала бы, а наша высота осталась — вышло бы чужое соотношение сторон
    /// и растянутая картинка. Поэтому при своих размерах не пишем ничего.
    /// </summary>
    [Fact]
    public void Author_Own_Size_Is_Not_Overridden()
    {
        var html = Render($"![снимок](attachment:{Id}){{width=50}}", (800, 600));

        Assert.Contains("width=\"50\"", html);
        Assert.DoesNotContain("width=\"800\"", html);
        Assert.DoesNotContain("height=\"600\"", html);
    }

    /// <summary>
    /// Узкая полоска: потолок 418 * 200/5000 = 16.7px ужал бы картинку почти в ничто, а подделанный
    /// заголовок дал бы ноль и картинка исчезла бы совсем. Нижний зажим этого не допускает.
    /// </summary>
    [Fact]
    public void Extremely_Narrow_Image_Keeps_A_Visible_Width()
    {
        var html = Render($"![полоска](attachment:{Id})", (200, 5000));

        Assert.Contains("max-width:min(100%,24px)", html);
    }

    /// <summary>Ссылка на файл — не картинка: размеров у неё нет, а download обязателен.</summary>
    [Fact]
    public void File_Link_Gets_No_Size()
    {
        var html = Render($"[смета](attachment:{Id})", (800, 600));

        Assert.DoesNotContain("width=", html);
        Assert.Contains("download", html);
    }
}
