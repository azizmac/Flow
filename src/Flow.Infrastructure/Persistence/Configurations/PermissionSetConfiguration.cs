using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

/// <summary>Свои наборы прав (этап 4E): права — int[] (числа ProjectPermission); участия ссылаются с SetNull.</summary>
public sealed class PermissionSetConfiguration : IEntityTypeConfiguration<PermissionSet>
{
    public void Configure(EntityTypeBuilder<PermissionSet> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Name).IsRequired().HasMaxLength(PermissionSet.NameMaxLength);
        builder.Property(s => s.Description).HasMaxLength(PermissionSet.DescriptionMaxLength);
        builder.PrimitiveCollection<List<ProjectPermission>>("_permissions").HasColumnName("Permissions").IsRequired();
        builder.Ignore(s => s.Permissions);
        builder.HasIndex(s => s.Name).IsUnique();
    }
}
