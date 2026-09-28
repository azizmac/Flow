using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

/// <summary>Сохранённые фильтры (docs/TZ_task_views.md §7): владелец — Restrict (людей не удаляют), звёзды — каскадом от фильтра.</summary>
public sealed class SavedFilterConfiguration : IEntityTypeConfiguration<SavedFilter>, IEntityTypeConfiguration<SavedFilterStar>
{
    public void Configure(EntityTypeBuilder<SavedFilter> builder)
    {
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Name).IsRequired().HasMaxLength(SavedFilter.NameMaxLength);
        builder.Property(f => f.Query).IsRequired().HasMaxLength(SavedFilter.QueryMaxLength);
        builder.Property(f => f.View).IsRequired().HasMaxLength(20);
        builder.HasOne<User>().WithMany().HasForeignKey(f => f.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(f => f.OwnerId);
        builder.HasIndex(f => f.Visibility);
    }

    public void Configure(EntityTypeBuilder<SavedFilterStar> builder)
    {
        builder.HasKey(s => new { s.FilterId, s.UserId });
        builder.HasOne<SavedFilter>().WithMany().HasForeignKey(s => s.FilterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => s.UserId);
    }
}
