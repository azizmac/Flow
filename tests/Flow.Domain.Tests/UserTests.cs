using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

public class UserTests
{
    private static User CreateUser() => User.Create("ilya", "ilya@example.com", "Илья", "Моторин");

    [Fact]
    public void Create_Should_NormalizeAndTrimFields()
    {
        var user = User.Create("  Ilya.Motorin ", " Ilya@Example.COM ", " Илья ", " Моторин ");

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("ilya.motorin", user.Username);
        Assert.Equal("ilya@example.com", user.Email);
        Assert.Equal("Илья", user.FirstName);
        Assert.Equal("Моторин", user.LastName);
        Assert.Equal("Илья Моторин", user.FullName);
        Assert.True(user.IsActive);
        Assert.Null(user.DeactivatedAt);
        Assert.Empty(user.Links);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData("_ilya")]
    [InlineData("ilya_")]
    [InlineData("il ya")]
    [InlineData("илья")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789")]
    public void Create_Should_Throw_When_UsernameIsInvalid(string username)
    {
        Assert.Throws<ArgumentException>(() => User.Create(username, "a@b.c", "A", "B"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("@domain")]
    [InlineData("local@")]
    [InlineData("a@@b")]
    [InlineData("a b@c")]
    public void Create_Should_Throw_When_EmailIsInvalid(string email)
    {
        Assert.Throws<ArgumentException>(() => User.Create("ilya", email, "A", "B"));
    }

    [Theory]
    [InlineData("", "B")]
    [InlineData("A", "")]
    [InlineData("   ", "B")]
    public void Create_Should_Throw_When_NameIsEmpty(string firstName, string lastName)
    {
        Assert.Throws<ArgumentException>(() => User.Create("ilya", "a@b.c", firstName, lastName));
    }

    [Fact]
    public void ChangeName_Should_TrimBothParts()
    {
        var user = CreateUser();

        user.ChangeName(" Иван ", " Иванов ");

        Assert.Equal("Иван Иванов", user.FullName);
    }

    [Fact]
    public void ChangePhoneNumber_Should_NormalizeToE164()
    {
        var user = CreateUser();

        user.ChangePhoneNumber("+7 (999) 123-45-67");

        Assert.Equal("+79991234567", user.PhoneNumber);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("89991234567")]
    [InlineData("+0123456789")]
    [InlineData("+7abc")]
    public void ChangePhoneNumber_Should_Throw_When_Invalid(string phone)
    {
        var user = CreateUser();

        Assert.Throws<ArgumentException>(() => user.ChangePhoneNumber(phone));
    }

    [Fact]
    public void ChangePhoneNumber_Should_Clear_When_Null()
    {
        var user = CreateUser();
        user.ChangePhoneNumber("+79991234567");

        user.ChangePhoneNumber(null);

        Assert.Null(user.PhoneNumber);
    }

    [Fact]
    public void SetLink_Should_ReplaceExistingLinkOfSameType()
    {
        var user = CreateUser();

        user.SetLink(UserLinkType.GitHub, "https://github.com/old");
        user.SetLink(UserLinkType.GitHub, "https://github.com/new");
        user.SetLink(UserLinkType.Telegram, "https://t.me/ilya");

        Assert.Equal(2, user.Links.Count);
        var github = user.Links.Single(l => l.Type == UserLinkType.GitHub);
        Assert.Equal("https://github.com/new", github.Url);
        Assert.Equal(user.Id, github.UserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("github.com/ilya")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com")]
    public void SetLink_Should_Throw_When_UrlIsInvalid(string url)
    {
        var user = CreateUser();

        Assert.Throws<ArgumentException>(() => user.SetLink(UserLinkType.Website, url));
    }

    [Fact]
    public void RemoveLink_Should_NotThrow_When_LinkIsMissing()
    {
        var user = CreateUser();
        user.SetLink(UserLinkType.GitHub, "https://github.com/ilya");

        user.RemoveLink(UserLinkType.LinkedIn);
        user.RemoveLink(UserLinkType.GitHub);

        Assert.Empty(user.Links);
    }

    [Fact]
    public void ChangeAvatar_Should_ValidateUrl_And_AllowNull()
    {
        var user = CreateUser();

        user.ChangeAvatar("https://cdn.example.com/a.png");
        Assert.Equal("https://cdn.example.com/a.png", user.AvatarUrl);

        user.ChangeAvatar(null);
        Assert.Null(user.AvatarUrl);

        Assert.Throws<ArgumentException>(() => user.ChangeAvatar("not a url"));
    }

    [Fact]
    public void ChangeJobTitle_And_Bio_Should_TrimAndClear()
    {
        var user = CreateUser();

        user.ChangeJobTitle("  Backend-разработчик ");
        user.ChangeBio("   ");

        Assert.Equal("Backend-разработчик", user.JobTitle);
        Assert.Null(user.Bio);
        Assert.Throws<ArgumentException>(() => user.ChangeBio(new string('x', User.BioMaxLength + 1)));
    }

    [Fact]
    public void Deactivate_Then_Activate_Should_ToggleState()
    {
        var user = CreateUser();

        user.Deactivate();
        Assert.False(user.IsActive);
        Assert.NotNull(user.DeactivatedAt);
        Assert.Throws<InvalidOperationException>(user.Deactivate);

        user.Activate();
        Assert.True(user.IsActive);
        Assert.Null(user.DeactivatedAt);
        Assert.Throws<InvalidOperationException>(user.Activate);
    }
}
