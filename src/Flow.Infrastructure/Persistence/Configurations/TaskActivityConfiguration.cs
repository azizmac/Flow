using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class TaskActivityConfiguration : IEntityTypeConfiguration<TaskActivity>
{
    public void Configure(EntityTypeBuilder<TaskActivity> builder)
    {
        builder.HasKey(a => a.Id);

        // int, как StatusType и UserRole.
        builder.Property(a => a.Type).IsRequired();

        // Название задачи ≤ 200, Guid и даты короче; text без лимита — чтобы будущие типы (вложения) не упёрлись в длину.
        builder.Property(a => a.OldValue);

        builder.Property(a => a.NewValue);

        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(a => a.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.ActorId);

        builder.HasIndex(a => new { a.TaskId, a.CreatedAt });
    }
}
