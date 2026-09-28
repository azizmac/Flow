using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class BoardMemberConfiguration : IEntityTypeConfiguration<BoardMember>
{
    public void Configure(EntityTypeBuilder<BoardMember> builder)
    {
        // Одно участие на пару (проект, человек): роль меняется, а не добавляется вторая строка.
        builder.HasKey(m => new { m.BoardId, m.UserId });

        builder.Property(m => m.Role).IsRequired();
        builder.Property(m => m.AddedAt).IsRequired();

        // Проект удаляют вместе с участниками; людей не удаляют, а деактивируют — Restrict, как у исполнителя.
        builder.HasOne<Board>()
            .WithMany()
            .HasForeignKey(m => m.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.AddedById)
            .OnDelete(DeleteBehavior.Restrict);

        // Свой набор прав (этап 4E): удалённый набор возвращает участию права его роли.
        builder.HasOne<PermissionSet>()
            .WithMany()
            .HasForeignKey(m => m.PermissionSetId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(m => m.PermissionSetId);

        // «Мои проекты и роли» (GET /boards/my-access) идут от пользователя.
        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => m.AddedById);
    }
}
