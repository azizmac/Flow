using Flow.Ai.Agents;
using Flow.Ai.Client;
using Flow.Ai.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flow.Ai.DependencyInjection;

public static class FlowAiServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует Flow.Ai: клиент Ollama, опции из секции "Ollama" конфигурации, GeneratorAgent.
    /// Использование в Flow.Api/Program.cs: builder.Services.AddFlowAi(builder.Configuration);
    /// </summary>
    public static IServiceCollection AddFlowAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));

        services.AddHttpClient<IOllamaClient, OllamaClient>((sp, http) =>
        {
            var ollamaOptions = configuration.GetSection(OllamaOptions.SectionName).Get<OllamaOptions>()
                                 ?? new OllamaOptions();
            http.BaseAddress = new Uri(ollamaOptions.BaseUrl);
            http.Timeout = TimeSpan.FromSeconds(120);
        });

        services.AddScoped<GeneratorAgent>();

        return services;
    }
}
