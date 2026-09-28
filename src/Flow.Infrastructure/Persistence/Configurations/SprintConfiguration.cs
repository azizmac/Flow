using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class SprintConfiguration : IEntityTypeConfiguration<Sprint>
{
    /// <summary>Частичный unique-индекс «один активный спринт в проекте» — страховка к проверке в Application.</summary>
    public const string ActiveIndexName = "IX_Sprints_BoardId_Active";

    public void Configure(EntityTypeBuilder<Sprint> builder)
    {
        builder.ToTable("Sprints");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Name).IsRequired().HasMaxLength(Sprint.NameMaxLength);
        builder.Property(s => s.Goal).HasMaxLength(Sprint.GoalMaxLength);
        builder.Property(s => s.State).IsRequired();

        builder.HasOne<Board>()
            .WithMany()
            .HasForeignKey(s => s.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.BoardId, s.SortOrder });
        builder.HasIndex(s => s.BoardId)
            .IsUnique()
            .HasFilter($"\"{nameof(Sprint.State)}\" = {(int)SprintState.Active}")
            .HasDatabaseName(ActiveIndexName);

        // Снимок — история: FK на задачу нет намеренно, удалённая задача остаётся в «взято на старте».
        builder.OwnsMany(s => s.Commitments, c =>
        {
            c.ToTable("SprintCommitments");
            c.WithOwner().HasForeignKey(x => x.SprintId);
            c.HasKey(x => new { x.SprintId, x.TaskId, x.Kind });
            c.Property(x => x.StoryPoints).HasColumnType("numeric(5,1)");
        });

        builder.Navigation(s => s.Commitments).HasField("_commitments").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
