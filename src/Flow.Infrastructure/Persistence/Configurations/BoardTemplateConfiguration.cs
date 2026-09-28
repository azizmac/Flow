using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

/// <summary>Сохранённые шаблоны проектов (этап 3F): чертёж — jsonb; автор — Restrict (людей не удаляют).</summary>
public sealed class BoardTemplateConfiguration : IEntityTypeConfiguration<BoardTemplate>
{
    public void Configure(EntityTypeBuilder<BoardTemplate> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).IsRequired().HasMaxLength(BoardTemplate.NameMaxLength);
        builder.Property(t => t.Description).HasMaxLength(BoardTemplate.DescriptionMaxLength);
        builder.Property(t => t.Payload).IsRequired().HasColumnType("jsonb");
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => t.CreatedById);
    }
}
