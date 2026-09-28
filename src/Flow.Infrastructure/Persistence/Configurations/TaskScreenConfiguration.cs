using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class TaskScreenConfiguration : IEntityTypeConfiguration<TaskScreen>
{
    public void Configure(EntityTypeBuilder<TaskScreen> builder)
    {
        builder.ToTable("TaskScreens");
        builder.HasKey(s => s.Id);

        // Id задаёт домен: иначе новый экран уже сохранённого проекта EF примет за существующий и пошлёт UPDATE.
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Context).IsRequired();

        // Поля экрана читаются всегда целиком и только с ним — одна jsonb-колонка.
        builder.OwnsMany(s => s.Fields, f =>
        {
            f.ToJson("Fields");
            f.Property(x => x.Field);
            f.Property(x => x.Required);
            f.Property(x => x.Section);
        });
        builder.Navigation(s => s.Fields).HasField("_fields").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<TaskType>()
            .WithMany()
            .HasForeignKey(s => s.TaskTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Один экран на (тип, контекст); тип null — «для всех типов», и он тоже один.
        builder.HasIndex(s => new { s.BoardId, s.TaskTypeId, s.Context }).IsUnique().AreNullsDistinct(false);
    }
}
