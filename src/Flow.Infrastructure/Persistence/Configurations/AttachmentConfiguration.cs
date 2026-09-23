using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName).IsRequired().HasMaxLength(Attachment.FileNameMaxLength);

        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(Attachment.ContentTypeMaxLength);

        builder.Property(a => a.SizeBytes).IsRequired();

        builder.Property(a => a.ContentHash).IsRequired();

        // Размеры картинки: обнуляемые намеренно. Индекса нет и не нужно — по ним не фильтруют
        // и не сортируют, они едут вместе со строкой в списке вложений.
        builder.Property(a => a.Width);
        builder.Property(a => a.Height);

        builder.Property(a => a.StorageKey).IsRequired().HasMaxLength(Attachment.StorageKeyMaxLength);

        builder.Property(a => a.BoardId).IsRequired();

        builder.Property(a => a.UploadedAt).IsRequired();

        // Вложения живут ровно столько, сколько задача: каскад в БД. Объекты в хранилище удаляются
        // отдельно — в БД их нет (см. docs/TZ_attachments.md, «Порядок операций»).
        builder.HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(a => a.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        // Автора не удаляют, а деактивируют — Restrict страхует это на уровне БД, как у комментариев.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(a => a.UploadedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.TaskId, a.UploadedAt });

        builder.HasIndex(a => a.UploadedById);

        // Повтор одного файла в одной задаче отсекается и на уровне БД, а не только проверкой в хендлере.
        builder.HasIndex(a => new { a.TaskId, a.ContentHash }).IsUnique();
    }
}
