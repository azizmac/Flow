using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Code)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion(code => code.Value, value => TaskCode.FromValue(value));

        builder.HasIndex(t => t.Code).IsUnique();

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Description).HasMaxLength(4000);

        builder.Property(t => t.CreatedAt).IsRequired();

        // DateOnly → date в Postgres (Npgsql маппит сам).
        builder.Property(t => t.DueDate);

        builder.HasOne<Status>()
            .WithMany()
            .HasForeignKey(t => t.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, а не SetNull: пользователей не удаляют, а деактивируют (см. docs/TZ_user.md);
        // FK страхует это на уровне БД — удалить пользователя с назначенными задачами нельзя.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.AssigneeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.AssigneeId);

        // Кто создал — для «своей задачи» у Member (docs/TZ_user_roles.md). null у задач, созданных до ролей.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.CreatedById);
    }
}
