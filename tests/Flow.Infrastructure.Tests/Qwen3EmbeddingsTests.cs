using Flow.Infrastructure.Search;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Постобработка векторов (ТЗ поиска, этап 2.2). Живёт здесь, а не в Flow.Application.Tests: сам
/// <see cref="Qwen3Embeddings"/> — internal-деталь Flow.Infrastructure, а этот проект уже держит
/// такие же юнит-тесты без контейнеров (AuthAccountServiceTests).
/// </summary>
public class Qwen3EmbeddingsTests
{
    [Fact]
    public void Shrink_Should_Keep_Unit_Length_After_Cutting()
    {
        // MRL: первые N координат — самостоятельное представление, но обрезка ломает длину вектора.
        var vector = Enumerable.Range(1, 1024).Select(i => (float)i).ToArray();
        var normalized = Qwen3Embeddings.Normalize(vector);

        var shrunk = Qwen3Embeddings.Shrink(normalized, 512);

        Assert.Equal(512, shrunk.Length);
        Assert.Equal(1f, Qwen3Embeddings.Norm(shrunk), 3);
    }

    [Fact]
    public void Shrink_Should_Keep_Already_Normalized_Vector_Untouched()
    {
        var vector = new float[512];
        vector[0] = 1f;

        var shrunk = Qwen3Embeddings.Shrink(vector, 512);

        Assert.Same(vector, shrunk);
    }

    [Fact]
    public void Normalize_Should_Scale_Unnormalized_Vector()
    {
        var vector = new[] { 3f, 4f };

        var normalized = Qwen3Embeddings.Normalize(vector);

        Assert.Equal(0.6f, normalized[0], 4);
        Assert.Equal(0.8f, normalized[1], 4);
    }

    [Fact]
    public void Normalize_Should_Reject_Zero_Vector() =>
        Assert.Throws<InvalidOperationException>(() => Qwen3Embeddings.Normalize(new float[512]));

    [Fact]
    public void Shrink_Should_Explain_Wrong_Dimension_From_Server()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Qwen3Embeddings.Shrink(new float[256], 512));

        Assert.Contains("256", error.Message);
        Assert.Contains("512", error.Message);
    }

    [Fact]
    public void WrapQuery_Should_Add_Instruct_Prefix() =>
        Assert.Equal("Instruct: найди задачи\nQuery: экспорт PDF",
            Qwen3Embeddings.WrapQuery("найди задачи", "экспорт PDF"));

    [Fact]
    public void WrapQuery_Should_Leave_Query_Alone_Without_Instruction() =>
        Assert.Equal("экспорт PDF", Qwen3Embeddings.WrapQuery("  ", "экспорт PDF"));

    [Fact]
    public void ModelVersion_Should_Change_With_Model_Dimensions_And_Instruction()
    {
        var baseline = Qwen3Embeddings.BuildModelVersion("Qwen3-Embedding-0.6B", 512, "инструкция");

        Assert.Equal(baseline, Qwen3Embeddings.BuildModelVersion("Qwen3-Embedding-0.6B", 512, "инструкция"));
        Assert.NotEqual(baseline, Qwen3Embeddings.BuildModelVersion("Qwen3-Embedding-4B", 512, "инструкция"));
        Assert.NotEqual(baseline, Qwen3Embeddings.BuildModelVersion("Qwen3-Embedding-0.6B", 1024, "инструкция"));
        Assert.NotEqual(baseline, Qwen3Embeddings.BuildModelVersion("Qwen3-Embedding-0.6B", 512, "другая инструкция"));
        Assert.StartsWith("Qwen3-Embedding-0.6B:512:", baseline);
    }
}
