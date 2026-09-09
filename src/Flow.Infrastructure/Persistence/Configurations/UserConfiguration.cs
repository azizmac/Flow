using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(User.UsernameMaxLength);

        // Значения уже нормализованы доменом (lower), поэтому обычный unique-индекс даёт регистронезависимую уникальность.
        builder.HasIndex(u => u.Username).IsUnique();

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(User.EmailMaxLength);

        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.FirstName)
            .IsRequired()
            .HasMaxLength(User.NameMaxLength);

        builder.Property(u => u.LastName)
            .IsRequired()
            .HasMaxLength(User.NameMaxLength);

        // Вычисляется из FirstName + LastName, в БД не хранится.
        builder.Ignore(u => u.FullName);

        builder.Property(u => u.AvatarUrl).HasMaxLength(UserLink.MaxUrlLength);

        builder.Property(u => u.JobTitle).HasMaxLength(User.JobTitleMaxLength);

        builder.Property(u => u.Bio).HasMaxLength(User.BioMaxLength);

        builder.Property(u => u.PhoneNumber).HasMaxLength(User.PhoneNumberMaxLength);

        // Роль и статус — int (как StatusType у задач). IsActive/CanBeAssigned вычисляются из Status и в БД не хранятся:
        // запросы фильтруют по Status (см. UserRepository), иначе EF не сможет транслировать вычисляемое свойство.
        builder.Property(u => u.Role).IsRequired();

        builder.Property(u => u.Status).IsRequired();

        builder.Property(u => u.StatusChangedAt);

        builder.Ignore(u => u.IsActive);

        builder.Ignore(u => u.CanBeAssigned);

        builder.Property(u => u.CreatedAt).IsRequired();

        // Ссылки — owned-коллекция: живут только внутри агрегата User, своего DbSet и репозитория у них нет.
        // Ключ (UserId, Type) в БД дублирует доменный инвариант «не более одной ссылки каждого типа».
        builder.OwnsMany(u => u.Links, links =>
        {
            links.ToTable("UserLinks");
            links.WithOwner().HasForeignKey(l => l.UserId);
            links.HasKey(l => new { l.UserId, l.Type });
            links.Property(l => l.Url)
                .IsRequired()
                .HasMaxLength(UserLink.MaxUrlLength);
        });

        builder.Navigation(u => u.Links).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
