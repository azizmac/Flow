using Flow.Domain.Entities;
using Flow.Shared.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class StatusConfiguration : IEntityTypeConfiguration<Status>
{
    public void Configure(EntityTypeBuilder<Status> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => StatusId.Create(value));

        builder.Property(s => s.BoardId)
            .HasConversion(id => id.Value, value => BoardId.Create(value));

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.SortOrder).IsRequired();

        builder.Property(s => s.IsInitial).IsRequired();

        builder.Property(s => s.IsFinal).IsRequired();

        builder.HasIndex(s => new { s.BoardId, s.SortOrder }).IsUnique();

        builder.HasIndex(s => new { s.BoardId, s.Name }).IsUnique();
    }
}
