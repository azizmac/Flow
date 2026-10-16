using Flow.Domain.Entities;
using Flow.Domain.Entities.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

// Провайдеры моделей и ключи агента (OpenCode). Ключ API — только зашифрованный DataProtection (ApiKeyProtected),
// модели кастомного провайдера — jsonb: их читают и пишут всегда целиком.

internal sealed class AgentProviderConfiguration : IEntityTypeConfiguration<AgentProvider>
{
    public void Configure(EntityTypeBuilder<AgentProvider> builder)
    {
        builder.ToTable("AgentProviders");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.ProviderKey).HasMaxLength(AgentProvider.KeyMaxLength).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(AgentProvider.NameMaxLength).IsRequired();
        builder.Property(p => p.BaseUrl).HasMaxLength(AgentProvider.UrlMaxLength);
        builder.Property(p => p.KeyHint).HasMaxLength(16);
        builder.Property(p => p.ApplyError).HasMaxLength(AgentProvider.ErrorMaxLength);

        builder.OwnsMany(p => p.Models, m =>
        {
            m.ToJson("Models");
            m.Property(x => x.Id);
            m.Property(x => x.Name);
        });
        builder.Navigation(p => p.Models).HasField("_models").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => p.ProviderKey).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.CreatedById).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AgentSettingsConfiguration : IEntityTypeConfiguration<AgentSettings>
{
    public void Configure(EntityTypeBuilder<AgentSettings> builder)
    {
        builder.ToTable("AgentSettings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.ProviderKey).HasMaxLength(AgentProvider.KeyMaxLength);
        builder.Property(s => s.ModelId).HasMaxLength(AgentSettings.ModelIdMaxLength);
    }
}
