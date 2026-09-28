using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Группы (этап 4C): состав и роли в проектах уходят каскадом вместе с группой; людей не удаляют — Restrict.
/// Индексы идут от человека: проверка прав спрашивает «в каких группах он и какие у них роли в этом проекте».
/// </summary>
public sealed class GroupConfiguration : IEntityTypeConfiguration<Group>, IEntityTypeConfiguration<GroupMember>, IEntityTypeConfiguration<BoardGroup>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Name).IsRequired().HasMaxLength(Group.NameMaxLength);
        builder.Property(g => g.Description).HasMaxLength(Group.DescriptionMaxLength);
        // Регистр имени проверяет Application; индекс страхует гонку точных дублей.
        builder.HasIndex(g => g.Name).IsUnique();
    }

    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.HasKey(m => new { m.GroupId, m.UserId });
        builder.HasOne<Group>().WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => m.UserId);
    }

    public void Configure(EntityTypeBuilder<BoardGroup> builder)
    {
        builder.HasKey(g => new { g.BoardId, g.GroupId });
        builder.Property(g => g.Role).IsRequired();
        builder.HasOne<Board>().WithMany().HasForeignKey(g => g.BoardId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Group>().WithMany().HasForeignKey(g => g.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(g => g.AddedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(g => g.GroupId);
        builder.HasIndex(g => g.AddedById);
    }
}
