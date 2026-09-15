using Flow.Infrastructure.Search;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Постобработка ответа эмбеддера — чистые функции, ни сети, ни модели, ни контейнера.
/// </summary>
public class Qwen3EmbeddingsTests
{
    private static float[] Ramp(int length) =>
        Enumerable.Range(1, length).Select(i => (float)i).ToArray();

    [Fact]
    public void Reduce_Truncates_And_Renormalizes()
    {
        var reduced = Qwen3Embeddings.Reduce(Ramp(1024), dimensions: 512);

        Assert.Equal(512, reduced.Length);
        // Урезание без перенормализации дало бы норму меньше единицы — и косинус перестал бы сравниваться.
        Assert.Equal(1.0, Qwen3Embeddings.Norm(reduced), Qwen3Embeddings.NormTolerance);
    }

    [Fact]
    public void Reduce_Keeps_Direction_Of_Already_Normalized_Vector()
    {
        var source = Qwen3Embeddings.Normalize(Ramp(512));

        var reduced = Qwen3Embeddings.Reduce(source.ToArray(), dimensions: 512);

        Assert.Equal(1.0, Qwen3Embeddings.Norm(reduced), Qwen3Embeddings.NormTolerance);
        for (var i = 0; i < source.Length; i++)
            Assert.Equal(source[i], reduced[i], 5);
    }

    [Fact]
    public void Reduce_Throws_When_Server_Returned_Fewer_Dimensions()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Qwen3Embeddings.Reduce(Ramp(256), dimensions: 512));

        Assert.Contains("256", error.Message);
        Assert.Contains("512", error.Message);
    }

    [Fact]
    public void Normalize_Throws_On_Zero_Vector() =>
        Assert.Throws<InvalidOperationException>(() => Qwen3Embeddings.Normalize(new float[512]));

    [Fact]
    public void WrapQuery_Adds_Instruction_To_Query_Only()
    {
        var wrapped = Qwen3Embeddings.WrapQuery("Retrieve relevant tasks", "экспорт падает");

        Assert.Equal("Instruct: Retrieve relevant tasks\nQuery: экспорт падает", wrapped);
    }

    [Fact]
    public void ModelVersion_Changes_With_Model_Dimensions_And_Instruction()
    {
        var baseline = Qwen3Embeddings.BuildModelVersion("Qwen3-Embedding-0.6B", 512, "retrieve");

        Assert.Equal(baseline, Qwen3Embeddings.BuildModelVersion("Qwen3-Embedding-0.6B", 512, "retrieve"));
        Assert.NotEqual(baseline, Qwen3Embeddings.BuildModelVersion("Qwen3-Embedding-4B", 512, "retrieve"));
        Assert.NotEqual(baseline, Qwen3Embeddings.BuildModelVersion("Qwen3-Embedding-0.6B", 1024, "retrieve"));
        Assert.NotEqual(baseline, Qwen3Embeddings.BuildModelVersion("Qwen3-Embedding-0.6B", 512, "retrieve tasks"));
    }

    [Fact]
    public void ModelVersion_Is_Model_Dimensions_And_Instruction_Hash()
    {
        var version = Qwen3Embeddings.BuildModelVersion("Qwen3-Embedding-0.6B", 512, "retrieve");

        var parts = version.Split(':');
        Assert.Equal("Qwen3-Embedding-0.6B", parts[0]);
        Assert.Equal("512", parts[1]);
        Assert.Equal(8, parts[2].Length);
    }
}
