using System.Text.Json;
using Flow.Application.Abstractions;
using Flow.Application.DependencyInjection;
using Flow.Application.Features.Search.Queries.SearchQuery;
using Flow.Application.Tests.Fakes;
using Flow.Infrastructure.DependencyInjection;
using Flow.Shared.Contracts.Search;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Качество выдачи на реальной базе: набор запросов с ожидаемым топом, recall@10 и MRR.
/// Это единственный способ заметить, что «поиск стал хуже» после смены модели, размерности,
/// инструкции или правил чанкинга — прогоняется вручную, числа фиксируются в описании PR.
/// <code>
/// docker compose -f docker-compose.data.yml --profile ai up -d embeddings
/// FLOW_GOLDEN_SET=1 dotnet test tests/Flow.Infrastructure.Tests --filter "Category=Golden"
/// </code>
/// Без FLOW_GOLDEN_SET=1 ничего не проверяет: набор привязан к тестовой базе разработчика,
/// и в обычном прогоне <c>dotnet test Flow.slnx</c> ему делать нечего.
/// </summary>
[Trait("Category", "Golden")]
public class GoldenSetTests(ITestOutputHelper output)
{
    private const int TopN = 10;

    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Golden_Set_Recall_And_Mrr()
    {
        if (Environment.GetEnvironmentVariable("FLOW_GOLDEN_SET") != "1")
        {
            output.WriteLine("FLOW_GOLDEN_SET=1 не выставлен — набор пропущен.");
            return;
        }

        var goldenSet = Load();

        await using var services = Build();
        await using (var probe = services.CreateAsyncScope())
        {
            if (!await probe.ServiceProvider.GetRequiredService<IEmbeddingGenerator>().IsAvailableAsync(CancellationToken.None))
            {
                output.WriteLine("Эмбеддер недоступен — поднимите профиль ai, иначе мерить нечего.");
                return;
            }
        }

        double recallSum = 0;
        double mrrSum = 0;
        var misses = new List<string>();

        output.WriteLine($"{"recall",-7}{"MRR",-7}запрос");
        output.WriteLine(new string('-', 78));

        foreach (var entry in goldenSet.Queries)
        {
            await using var scope = services.CreateAsyncScope();
            var response = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
                new SearchQuery(Owner, entry.Query, null, null, IncludeArchived: true, SearchMode.Hybrid, TopN, 0));

            // Комментарий засчитывается за свою задачу: человек искал задачу, а нашёлся её обсуждение —
            // это попадание, а не промах.
            var codes = response!.Items.Select(item => item.TaskCode ?? string.Empty).ToList();

            var ranks = entry.Expected
                .Select(code => codes.FindIndex(found => string.Equals(found, code, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            var found = ranks.Count(rank => rank >= 0);
            var recall = (double)found / entry.Expected.Count;
            var best = ranks.Where(rank => rank >= 0).DefaultIfEmpty(-1).Min();
            var mrr = best >= 0 ? 1.0 / (best + 1) : 0;

            recallSum += recall;
            mrrSum += mrr;

            output.WriteLine($"{recall,-7:F2}{mrr,-7:F2}{entry.Query}");

            if (found < entry.Expected.Count)
            {
                var lost = entry.Expected.Where((_, i) => ranks[i] < 0);
                misses.Add($"  «{entry.Query}» — не нашлось: {string.Join(", ", lost)}; выдача: {string.Join(", ", codes.Where(code => code.Length > 0).Take(5))}");
            }
        }

        var meanRecall = recallSum / goldenSet.Queries.Count;
        var meanMrr = mrrSum / goldenSet.Queries.Count;

        output.WriteLine(new string('-', 78));
        output.WriteLine($"запросов: {goldenSet.Queries.Count}   recall@{TopN}: {meanRecall:F3}   MRR: {meanMrr:F3}");

        if (misses.Count > 0)
        {
            output.WriteLine("");
            output.WriteLine("Промахи:");
            foreach (var miss in misses)
                output.WriteLine(miss);
        }

        Assert.True(meanRecall >= goldenSet.MinRecall, $"recall@{TopN} {meanRecall:F3} ниже порога {goldenSet.MinRecall:F2}");
        Assert.True(meanMrr >= goldenSet.MinMrr, $"MRR {meanMrr:F3} ниже порога {goldenSet.MinMrr:F2}");
    }

    private static GoldenSet Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GoldenSet", "golden-set.json");
        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<GoldenSet>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"Не удалось прочитать {path}.");
    }

    /// <summary>
    /// Тот же путь, что и в приложении: реальная база, реальная модель, тот же хендлер поиска.
    /// Мерить качество на фейках бессмысленно — они не про смысл.
    /// </summary>
    private static ServiceProvider Build()
    {
        var connection = Environment.GetEnvironmentVariable("FLOW_GOLDEN_POSTGRES")
            ?? "Host=localhost;Port=5433;Database=flow;Username=flow;Password=flow";

        var endpoint = Environment.GetEnvironmentVariable("FLOW_TEST_EMBEDDINGS_ENDPOINT") ?? "http://localhost:8081/v1";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = connection,
                ["Search:Enabled"] = "true",
                ["Search:Indexing:Enabled"] = "false",
                ["Search:Embeddings:QueryEndpoint"] = endpoint,
                ["Search:Embeddings:Model"] = Environment.GetEnvironmentVariable("FLOW_TEST_EMBEDDINGS_MODEL") ?? "Qwen3-Embedding-0.6B",
                ["Search:Embeddings:TimeoutSeconds"] = "30"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlowInfrastructure(configuration);
        services.AddFlowApplication();
        services.AddSingleton<IAccountService, AlwaysSucceedingAccountService>();
        return services.BuildServiceProvider();
    }

    private sealed record GoldenSet(double MinRecall, double MinMrr, IReadOnlyList<GoldenQuery> Queries);

    private sealed record GoldenQuery(string Query, IReadOnlyList<string> Expected);
}
