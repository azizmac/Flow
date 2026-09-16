using System.Security.Cryptography;
using System.Text;

namespace Flow.Infrastructure.Search;

/// <summary>
/// Постобработка ответа эмбеддера — чистые функции, вынесены отдельно, чтобы проверяться юнит-тестами
/// без сети и без модели. Pooling по последнему токену делает сам llama-server; здесь только то, что
/// обязано случиться на нашей стороне: MRL-урезание, нормализация и версия модели.
/// </summary>
internal static class Qwen3Embeddings
{
    /// <summary>Допуск проверки |v| ≈ 1: fp16 и урезание дают заметно больше машинного эпсилона.</summary>
    public const float NormTolerance = 1e-3f;

    /// <summary>
    /// Приводит ответ сервера к рабочему виду: Matryoshka-урезание до нужной размерности и L2-нормализация.
    /// Урезание без перенормализации ломает косинус — норма обрезанного вектора меньше единицы, и
    /// расстояния перестают сравниваться между собой.
    /// </summary>
    public static float[] Reduce(IReadOnlyList<float> raw, int dimensions)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (dimensions <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions, "Размерность вектора должна быть положительной.");

        if (raw.Count < dimensions)
            throw new InvalidOperationException(
                $"Эмбеддер вернул вектор размерности {raw.Count}, а настроено {dimensions}: " +
                "урезать (MRL) можно только вниз. Проверьте Search:Embeddings:Model и Dimensions.");

        var reduced = new float[dimensions];
        for (var i = 0; i < dimensions; i++)
            reduced[i] = raw[i];

        return Normalize(reduced);
    }

    /// <summary>L2-нормализация на месте. Нулевой вектор оставляем как есть — делить не на что.</summary>
    public static float[] Normalize(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);

        double sum = 0;
        foreach (var value in vector)
            sum += (double)value * value;

        var norm = Math.Sqrt(sum);
        if (norm is 0 or double.NaN)
            throw new InvalidOperationException("Эмбеддер вернул нулевой вектор — нормализовать его нельзя.");

        for (var i = 0; i < vector.Length; i++)
            vector[i] = (float)(vector[i] / norm);

        return vector;
    }

    /// <summary>Длина вектора — для проверки «сервер уже отдал нормализованное» в тестах и диагностике.</summary>
    public static double Norm(IReadOnlyList<float> vector)
    {
        double sum = 0;
        foreach (var value in vector)
            sum += (double)value * value;

        return Math.Sqrt(sum);
    }

    /// <summary>
    /// Запрос уходит в модель с инструкцией, документ — как есть. Асимметрия у Qwen3-Embedding
    /// обязательна: без префикса качество падает молча, ошибок никто не увидит.
    /// </summary>
    public static string WrapQuery(string instruction, string query) =>
        string.IsNullOrWhiteSpace(instruction) ? query : $"Instruct: {instruction}\nQuery: {query}";

    /// <summary>
    /// {модель}:{размерность}:{8 символов SHA-256 от инструкции}. Смена любой части означает, что
    /// старые векторы несравнимы с новыми: поиск читает только текущую версию, старые чанки удаляются
    /// после переиндексации.
    /// </summary>
    public static string BuildModelVersion(string model, int dimensions, string instruction)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(instruction ?? string.Empty)));
        return $"{model}:{dimensions}:{hash[..8]}";
    }
}
