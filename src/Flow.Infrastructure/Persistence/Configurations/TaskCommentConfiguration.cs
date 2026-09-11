using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Body)
            .IsRequired()
            .HasMaxLength(TaskComment.BodyMaxLength);

        builder.Property(c => c.CreatedAt).IsRequired();

        builder.Property(c => c.EditedAt);

        // Комментарии живут ровно столько, сколько задача: каскад в БД, отдельно их никто не удаляет.
        builder.HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(c => c.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        // Автора не удаляют, а деактивируют (docs/TZ_user.md) — Restrict страхует это на уровне БД.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.AuthorId);

        builder.HasIndex(c => new { c.TaskId, c.CreatedAt });

        // Упоминания — owned-коллекция по образцу UserLinks: своего DbSet нет, подгружаются вместе с комментарием.
        builder.OwnsMany(c => c.Mentions, mentions =>
        {
            mentions.ToTable("TaskCommentMentions");
            mentions.WithOwner().HasForeignKey(m => m.CommentId);
            mentions.HasKey(m => new { m.CommentId, m.UserId });
            mentions.HasOne<User>()
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Navigation(c => c.Mentions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
