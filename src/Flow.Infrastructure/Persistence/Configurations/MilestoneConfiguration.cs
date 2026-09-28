using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class MilestoneConfiguration : IEntityTypeConfiguration<Milestone>
{
    public void Configure(EntityTypeBuilder<Milestone> builder)
    {
        builder.ToTable("Milestones");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Name).IsRequired().HasMaxLength(Milestone.NameMaxLength);
        builder.Property(m => m.Description).HasMaxLength(Milestone.DescriptionMaxLength);
        builder.Property(m => m.State).IsRequired();
        // Общая веха (этап 2H) — массивом, как TaskTypeIds у пользовательских полей: связь нужна только «в каких проектах
        // видна», а удалённый проект из массива просто перестаёт что-либо значить.
        builder.PrimitiveCollection<List<Guid>>("_sharedBoardIds").HasColumnName("SharedBoardIds").IsRequired();
        builder.Ignore(m => m.SharedBoardIds);

        builder.HasOne<Board>()
            .WithMany()
            .HasForeignKey(m => m.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        // Регистр имени проверяет Application (FQL ищет вехи без учёта регистра); индекс страхует гонку точных дублей.
        builder.HasIndex(m => new { m.BoardId, m.Name }).IsUnique();
        builder.HasIndex(m => new { m.BoardId, m.SortOrder });
    }
}
