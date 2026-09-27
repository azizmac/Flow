using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> builder)
    {
        builder.ToTable("CustomFields");
        builder.HasKey(f => f.Id);

        // Id задаёт домен: иначе новое поле уже сохранённого проекта EF примет за существующее и пошлёт UPDATE.
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Key).IsRequired().HasMaxLength(30);
        builder.Property(f => f.Name).IsRequired().HasMaxLength(CustomFieldDefinition.NameMaxLength);
        builder.Property(f => f.Type).IsRequired();
        builder.Ignore(f => f.HasOptions);

        // Варианты живут только внутри поля — одна jsonb-колонка, а не таблица: их читают всегда целиком.
        builder.OwnsMany(f => f.Options, o =>
        {
            o.ToJson("Options");
            o.Property(x => x.Id);
            o.Property(x => x.Label);
            o.Property(x => x.Color);
        });
        builder.Navigation(f => f.Options).HasField("_options").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.PrimitiveCollection<List<Guid>>("_taskTypeIds").HasColumnName("TaskTypeIds").IsRequired();
        builder.Ignore(f => f.TaskTypeIds);

        // Key неизменяем и уникален в проекте: по нему FQL находит поле. Регистр ключа фиксирован регуляркой.
        builder.HasIndex(f => new { f.BoardId, f.Key }).IsUnique();
    }
}
