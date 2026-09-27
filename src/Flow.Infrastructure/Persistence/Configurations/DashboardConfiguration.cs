using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

/// <summary>Дашборды (docs/TZ_task_views.md §8): владелец — Restrict (людей не удаляют), виджеты — каскадом, настройки — jsonb.</summary>
public sealed class DashboardConfiguration : IEntityTypeConfiguration<Dashboard>, IEntityTypeConfiguration<DashboardWidget>
{
    public void Configure(EntityTypeBuilder<Dashboard> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Name).IsRequired().HasMaxLength(Dashboard.NameMaxLength);
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.OwnerId);
        builder.HasIndex(d => d.Visibility);
        builder.HasMany(d => d.Widgets).WithOne().HasForeignKey(w => w.DashboardId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(d => d.Widgets).HasField("_widgets").UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    public void Configure(EntityTypeBuilder<DashboardWidget> builder)
    {
        builder.ToTable("DashboardWidgets");
        builder.HasKey(w => w.Id);
        // Id задаёт домен: иначе новый виджет уже сохранённого дашборда EF примет за существующий и пошлёт UPDATE.
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Property(w => w.Title).HasMaxLength(DashboardWidget.TitleMaxLength);
        builder.Property(w => w.Config).IsRequired().HasColumnType("jsonb");
    }
}
