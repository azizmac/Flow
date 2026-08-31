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

        builder.Navigation(b => b.Statuses).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(b => b.Tasks).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(b => b.Statuses)
            .WithOne()
            .HasForeignKey(s => s.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(b => b.Tasks)
            .WithOne()
            .HasForeignKey(t => t.BoardId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
