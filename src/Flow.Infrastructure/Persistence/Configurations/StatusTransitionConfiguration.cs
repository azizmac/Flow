using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Переходы workflow (docs/TZ_workflow_config.md §2) — часть агрегата Board. Id задаёт домен (ValueGeneratedNever,
/// как у TaskType и Status). FK на статусы — каскадом: удалить статус значит убрать и его переходы (домен делает
/// то же в RemoveStatus). Unique (BoardId, FromStatusId, ToStatusId) с NULLS NOT DISTINCT: иначе Postgres считал бы
/// два перехода «из любого» в один статус разными.
/// </summary>
public sealed class StatusTransitionConfiguration : IEntityTypeConfiguration<StatusTransition>
{
    public void Configure(EntityTypeBuilder<StatusTransition> builder)
    {
        builder.ToTable("StatusTransitions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(StatusTransition.NameMaxLength);
        builder.Ignore(t => t.Conditions);
        builder.Ignore(t => t.RequireFields);
        builder.PrimitiveCollection<List<Guid>>("_requireFields").HasColumnName("RequireFields").IsRequired();

        builder.HasOne<Status>().WithMany().HasForeignKey(t => t.FromStatusId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Status>().WithMany().HasForeignKey(t => t.ToStatusId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => new { t.BoardId, t.FromStatusId, t.ToStatusId }).IsUnique().AreNullsDistinct(false);
        builder.HasIndex(t => t.ToStatusId);
        builder.HasIndex(t => t.FromStatusId);
    }
}
