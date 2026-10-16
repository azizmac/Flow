using Flow.Domain.Entities.Agents;
using Xunit;

namespace Flow.Domain.Tests;

public class AgentProviderTests
{
    private static readonly Guid Author = Guid.NewGuid();

    [Theory]
    [InlineData("OpenRouter", "openrouter")]
    [InlineData(" my-llm.v2 ", "my-llm.v2")]
    public void Key_Is_Normalized(string key, string expected) =>
        Assert.Equal(expected, AgentProvider.CreateCatalog(key, "X", Author).ProviderKey);

    [Theory]
    [InlineData("")]
    [InlineData("-bad")]
    [InlineData("has space")]
    [InlineData("../etc")]
    public void Bad_Key_Is_Rejected(string key) =>
        Assert.Throws<ArgumentException>(() => AgentProvider.CreateCatalog(key, "X", Author));

    [Fact]
    public void Custom_Provider_Needs_Http_Address_And_Models()
    {
        Assert.Throws<ArgumentException>(() => AgentProvider.CreateCustom("my", "Мой", "file:///etc/passwd", [("m", "M")], Author));
        Assert.Throws<ArgumentException>(() => AgentProvider.CreateCustom("my", "Мой", "https://llm.example.com/v1", [], Author));
        Assert.Throws<ArgumentException>(() => AgentProvider.CreateCustom("my", "Мой", "https://llm.example.com/v1", [("m", "A"), ("m", "B")], Author));

        var provider = AgentProvider.CreateCustom("my", "Мой", "https://llm.example.com/v1/", [("qwen", "")], Author);
        Assert.Equal("https://llm.example.com/v1", provider.BaseUrl);
        Assert.Equal("qwen", provider.Models.Single().Name);
    }

    [Fact]
    public void Catalog_Provider_Has_No_Address()
    {
        var provider = AgentProvider.CreateCatalog("anthropic", "Anthropic", Author);

        Assert.Throws<InvalidOperationException>(() => provider.UpdateCustom("X", "https://evil.example.com", [("m", "M")]));
    }

    [Fact]
    public void Key_Is_Set_And_Cleared_With_Author()
    {
        var provider = AgentProvider.CreateCatalog("anthropic", "Anthropic", Author);
        var editor = Guid.NewGuid();

        provider.SetKey("protected", AgentProvider.HintOf("sk-ant-0123456789wxyz"), editor, DateTime.UtcNow);
        Assert.Equal("…wxyz", provider.KeyHint);
        Assert.Equal(editor, provider.KeyUpdatedById);

        provider.ClearKey(Author, DateTime.UtcNow);
        Assert.Null(provider.ApiKeyProtected);
        Assert.Null(provider.KeyHint);
    }

    [Fact]
    public void Short_Key_Hint_Reveals_Nothing() => Assert.Equal("••••", AgentProvider.HintOf("abc123"));

    [Fact]
    public void Apply_Error_Keeps_Last_Success_Time()
    {
        var provider = AgentProvider.CreateCatalog("anthropic", "Anthropic", Author);
        var at = DateTime.UtcNow;
        provider.MarkApplied(null, true, at);
        Assert.True(provider.RestartRequired);

        provider.MarkApplied("OpenCode недоступен", true, at.AddMinutes(1));

        Assert.Equal(at, provider.AppliedAt);
        Assert.Equal("OpenCode недоступен", provider.ApplyError);
        Assert.False(provider.RestartRequired);
    }

    [Fact]
    public void Default_Model_Is_A_Pair()
    {
        var settings = AgentSettings.Create();

        Assert.Throws<ArgumentException>(() => settings.SetDefaultModel("openrouter", null, Author, DateTime.UtcNow));

        settings.SetDefaultModel("OpenRouter", " openai/gpt-4o-mini ", Author, DateTime.UtcNow);
        Assert.Equal(("openrouter", "openai/gpt-4o-mini"), (settings.ProviderKey, settings.ModelId));

        settings.SetDefaultModel(null, null, Author, DateTime.UtcNow);
        Assert.Null(settings.ProviderKey);
    }
}
