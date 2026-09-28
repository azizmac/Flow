using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class BoardConfiguration : IEntityTypeConfiguration<Board>
{
    public void Configure(EntityTypeBuilder<Board> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Key)
            .IsRequired()
            .HasMaxLength(10);

        builder.HasIndex(b => b.Key).IsUnique();

        builder.Property(b => b.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(b => b.CreatedAt).IsRequired();

        builder.Property(b => b.NextTaskNumber).IsRequired();

        // null — роль в проекте равна глобальной (docs/TZ_project_access.md): у существующих проектов так и есть.
        builder.Property(b => b.DefaultRole);

        // Open = 0 — у всех существующих проектов: после миграции каждый видит ровно то, что видел.
        builder.Property(b => b.Visibility).IsRequired();

        builder.Navigation(b => b.Statuses).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(b => b.Tasks).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(b => b.TaskTypes).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(b => b.Transitions).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(b => b.CustomFields).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(b => b.Screens).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Free = 0 — у всех существующих проектов: после миграции статус меняется как раньше.
        builder.Property(b => b.WorkflowMode).IsRequired();
        builder.Property(b => b.DoneColumnDays).IsRequired().HasDefaultValue(Board.DefaultDoneColumnDays);

        builder.HasMany(b => b.Transitions)
            .WithOne()
            .HasForeignKey(t => t.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.Statuses)
            .WithOne()
            .HasForeignKey(s => s.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.Screens)
            .WithOne()
            .HasForeignKey(s => s.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.CustomFields)
            .WithOne()
            .HasForeignKey(f => f.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.TaskTypes)
            .WithOne()
            .HasForeignKey(t => t.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.Tasks)
            .WithOne()
            .HasForeignKey(t => t.BoardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
