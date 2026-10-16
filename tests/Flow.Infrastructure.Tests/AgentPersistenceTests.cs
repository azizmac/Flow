using Flow.Domain.Entities.Agents;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>Провайдеры агента на реальном Postgres: модели — jsonb, Id провайдера уникален, ключ лежит только зашифрованным.</summary>
[Collection(PostgresCollection.Name)]
public class AgentPersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    [Fact]
    public async Task Provider_Round_Trips_With_Models_And_Protected_Key()
    {
        var provider = AgentProvider.CreateCustom("persist-llm", "Свой сервер", "http://host.docker.internal:1234/v1", [("qwen", "Qwen"), ("llama", "Llama")], Owner);
        provider.SetKey("protected-value", "…abcd", Owner, DateTime.UtcNow);
        await db.QueryAsync(async ctx =>
        {
            ctx.AgentProviders.Add(provider);
            return await ctx.SaveChangesAsync();
        });

        var loaded = await db.QueryAsync(ctx => ctx.AgentProviders.AsNoTracking().SingleAsync(p => p.Id == provider.Id));

        Assert.Equal(["qwen", "llama"], loaded.Models.Select(m => m.Id));
        Assert.Equal("protected-value", loaded.ApiKeyProtected);
        Assert.Equal("…abcd", loaded.KeyHint);
    }

    [Fact]
    public async Task Provider_Key_Is_Unique()
    {
        await db.QueryAsync(async ctx =>
        {
            ctx.AgentProviders.Add(AgentProvider.CreateCatalog("persist-dup", "Dup", Owner));
            return await ctx.SaveChangesAsync();
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.QueryAsync(async ctx =>
        {
            ctx.AgentProviders.Add(AgentProvider.CreateCatalog("persist-dup", "Dup", Owner));
            return await ctx.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task Settings_Row_Holds_The_Default_Model()
    {
        await db.QueryAsync(async ctx =>
        {
            var settings = await ctx.AgentSettings.FirstOrDefaultAsync(s => s.Id == AgentSettings.SingletonId);
            if (settings is null)
            {
                settings = AgentSettings.Create();
                ctx.AgentSettings.Add(settings);
            }

            settings.SetDefaultModel("openrouter", "openai/gpt-4o-mini", Owner, DateTime.UtcNow);
            return await ctx.SaveChangesAsync();
        });

        var loaded = await db.QueryAsync(ctx => ctx.AgentSettings.AsNoTracking().SingleAsync(s => s.Id == AgentSettings.SingletonId));
        Assert.Equal(("openrouter", "openai/gpt-4o-mini"), (loaded.ProviderKey, loaded.ModelId));
    }
}
