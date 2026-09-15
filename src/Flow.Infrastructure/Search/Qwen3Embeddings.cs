using System.Security.Cryptography;
using System.Text;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Постобработка векторов Qwen3-Embedding — чистые функции без состояния, покрыты юнит-тестами.
/// Здесь живёт всё, что легко сделать молча неправильно: MRL-урезание без перенормализации,
/// забытая инструкция у запроса, «почти единичный» вектор от сервера.
/// Pooling по последнему токену делает сам llama-server; в .NET он не повторяется.
/// </summary>
internal static class Qwen3Embeddings
{
    /// <summary>Допуск на длину вектора: half-точность и сеть дают расхождение в последних знаках.</summary>
    private const float NormTolerance = 1e-2f;

    /// <summary>
    /// Матрёшка (MRL): у Qwen3 первые N координат — самостоятельное представление, поэтому длинный
    /// вектор можно просто обрезать. Но обрезка ломает единичную длину, а косинус по halfvec_cosine_ops
    /// на ненормализованных векторах даёт другое ранжирование — поэтому сразу перенормализуем.
    /// </summary>
    public static float[] Shrink(float[] vector, int dimensions)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimensions, 1);

        if (vector.Length < dimensions)
            throw new InvalidOperationException(
                $"Эмбеддер вернул вектор размерности {vector.Length}, а нужно минимум {dimensions}: " +
                "проверьте Search:Embeddings:Model и Dimensions.");

        var shrunk = vector.Length == dimensions ? vector : vector[..dimensions];
        return Normalize(shrunk);
    }

    /// <summary>
    /// Приводит к единичной длине. Уже нормализованный вектор не портит (делится на ≈1),
    /// нулевой — отвергает: такой вектор не имеет направления и в косинусном индексе бесполезен.
    /// </summary>
    public static float[] Normalize(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);

        var norm = Norm(vector);
        if (norm <= float.Epsilon)
            throw new InvalidOperationException("Эмбеддер вернул нулевой вектор — нормализовать его нельзя.");

        if (Math.Abs(norm - 1f) <= NormTolerance)
            return vector;

        var normalized = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
            normalized[i] = vector[i] / norm;

        return normalized;
    }

    public static float Norm(float[] vector)
    {
        double sum = 0;
        foreach (var value in vector)
            sum += (double)value * value;

        return (float)Math.Sqrt(sum);
    }

    /// <summary>
    /// Запрос и документ кодируются по-разному: документ — как есть, запрос — с Instruct-префиксом.
    /// Асимметрия у Qwen3 обязательна, без неё качество падает молча.
    /// </summary>
    public static string WrapQuery(string instruction, string query) =>
        string.IsNullOrWhiteSpace(instruction)
            ? query
            : $"Instruct: {instruction}\nQuery: {query}";

    /// <summary>
    /// {модель}:{размерность}:{8 символов SHA-256 от инструкции}. Любая из трёх частей меняет способ
    /// кодирования, а значит, делает старые векторы несравнимыми с новыми — поэтому все три в версии.
    /// </summary>
    public static string BuildModelVersion(string model, int dimensions, string instruction)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(instruction ?? string.Empty)));
        return $"{model}:{dimensions}:{hash[..8]}";
    }
}
