using System.Text;
using Flow.Application.Features.Users.Commands.UserRemoveAvatarCommand;
using Flow.Application.Features.Users.Commands.UserSetAvatarCommand;
using Flow.Application.Features.Users.Queries.UserAvatarQuery;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Свой аватар: загрузка картинкой, проверка формата по сигнатуре, версионный адрес, уборка старого объекта
/// и отдача только текущего файла. Команды без UserId — чужой аватар поменять нечем, проверять это незачем.
/// </summary>
public class UserAvatarFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6];

    private static Task<UserResponse> UploadAsync(AttachmentTestContext context, string fileName, byte[] bytes, long? size = null, Guid? actorId = null) =>
        context.Mediator.Send(
            new UserSetAvatarCommand(actorId ?? Owner, fileName, size ?? bytes.Length, new MemoryStream(bytes)),
            CancellationToken.None);

    [Fact]
    public async Task Upload_Should_StoreObject_And_SetVersionedUrl()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();

        var user = await UploadAsync(context, "Моё фото.PNG", Png);

        Assert.NotNull(user.AvatarUrl);
        Assert.StartsWith($"/avatars/{Owner}/", user.AvatarUrl);
        Assert.EndsWith(".png", user.AvatarUrl);

        var key = Assert.Single(context.Storage.Objects.Keys);
        Assert.Equal($"avatars/{Owner}/{user.AvatarUrl![$"/avatars/{Owner}/".Length..]}", key);
        Assert.Equal(Png, context.Storage.Objects[key]);
    }

    [Fact]
    public async Task Reupload_Should_ChangeUrl_And_DeleteOldObject()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();

        var first = await UploadAsync(context, "a.png", Png);
        var second = await UploadAsync(context, "a.png", Png);

        Assert.NotEqual(first.AvatarUrl, second.AvatarUrl);
        var key = Assert.Single(context.Storage.Objects.Keys);
        Assert.EndsWith(second.AvatarUrl![second.AvatarUrl!.LastIndexOf('/')..], key);
    }

    [Theory]
    [InlineData("photo.svg")]
    [InlineData("photo.txt")]
    [InlineData("photo")]
    public async Task Upload_Should_Reject_NonRasterTypes(string fileName)
    {
        var context = TestMediatorFactory.CreateAttachmentContext();

        await Assert.ThrowsAsync<ArgumentException>(() => UploadAsync(context, fileName, Png));
        Assert.Empty(context.Storage.Objects);
    }

    [Fact]
    public async Task Upload_Should_Reject_FilePretendingToBeImage()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            UploadAsync(context, "evil.png", Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>")));
        Assert.Empty(context.Storage.Objects);
    }

    [Fact]
    public async Task Upload_Should_Reject_EmptyAndOversizedFiles()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();

        await Assert.ThrowsAsync<ArgumentException>(() => UploadAsync(context, "a.png", [], size: 0));
        await Assert.ThrowsAsync<ArgumentException>(() => UploadAsync(context, "a.png", Png, size: AvatarLimits.MaxBytes + 1));
        Assert.Empty(context.Storage.Objects);
    }

    [Fact]
    public async Task Upload_Should_BeAllowed_ForReader()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var reader = User.Create("reader", "reader@example.com", "Имя", "Фамилия");
        reader.ChangeRole(Flow.Domain.Entities.UserRole.Reader);
        context.Users.Add(reader);

        var user = await UploadAsync(context, "a.webp", [.."RIFF"u8, 0, 0, 0, 0, .."WEBP"u8], actorId: reader.Id);

        Assert.StartsWith($"/avatars/{reader.Id}/", user.AvatarUrl);
    }

    [Fact]
    public async Task Remove_Should_ClearUrl_And_DeleteObject()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        await UploadAsync(context, "a.png", Png);

        var user = await context.Mediator.Send(new UserRemoveAvatarCommand(Owner), CancellationToken.None);

        Assert.Null(user.AvatarUrl);
        Assert.Empty(context.Storage.Objects);

        // Повтор — не ошибка.
        var again = await context.Mediator.Send(new UserRemoveAvatarCommand(Owner), CancellationToken.None);
        Assert.Null(again.AvatarUrl);
    }

    [Fact]
    public async Task Query_Should_ServeOnlyCurrentFile()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var first = await UploadAsync(context, "a.png", Png);
        var second = await UploadAsync(context, "b.jpg", [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]);

        static string NameOf(UserResponse u) => u.AvatarUrl![(u.AvatarUrl!.LastIndexOf('/') + 1)..];

        var current = await context.Mediator.Send(new UserAvatarQuery(Owner, NameOf(second)), CancellationToken.None);
        Assert.NotNull(current);
        Assert.Equal("image/jpeg", current.ContentType);

        Assert.Null(await context.Mediator.Send(new UserAvatarQuery(Owner, NameOf(first)), CancellationToken.None));
        Assert.Null(await context.Mediator.Send(new UserAvatarQuery(Guid.NewGuid(), NameOf(second)), CancellationToken.None));
    }
}
