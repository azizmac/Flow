using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Code)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion(code => code.Value, value => TaskCode.FromValue(value));

        builder.HasIndex(t => t.Code).IsUnique();

        builder.Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Description).HasMaxLength(4000);

        builder.Property(t => t.CreatedAt).IsRequired();

        // DateOnly → date в Postgres (Npgsql маппит сам).
        builder.Property(t => t.DueDate);
        builder.Property(t => t.StartDate);

        builder.Property(t => t.Priority).IsRequired();

        // 0…999.9 с одним знаком — ровно numeric(4,1); берём (5,1) с запасом, границу держит домен.
        builder.Property(t => t.StoryPoints).HasPrecision(5, 1);

        builder.Property(t => t.EstimateMinutes);

        builder.Property(t => t.UpdatedAt).IsRequired();
        builder.Property(t => t.StatusChangedAt).IsRequired();

        // Restrict, как у статуса: тип удаляется только вместе с проектом, и тогда задачи уходят первыми
        // (BoardRepository.RemoveAsync). Индекс — под фильтр «все ошибки» (join TaskTypes по виду).
        builder.HasOne<TaskType>()
            .WithMany()
            .HasForeignKey(t => t.TypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.TypeId);

        builder.HasOne<Status>()
            .WithMany()
            .HasForeignKey(t => t.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, а не SetNull: пользователей не удаляют, а деактивируют (см. docs/TZ_user.md);
        // FK страхует это на уровне БД — удалить пользователя с назначенными задачами нельзя.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.AssigneeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.AssigneeId);

        // Кто создал — для «своей задачи» у Member (docs/TZ_user_roles.md). null у задач, созданных до ролей.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.CreatedById);

        // Иерархия (docs/TZ_task_model.md §3). Restrict, а не каскад: поддерево удаляет хендлер, иначе удаление
        // прошло бы мимо журнала, очереди индексации и зачистки вложений. Индекс — под «детей задачи» и счётчики.
        builder.HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(t => t.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.ParentId);

        // Спринт (docs/TZ_task_views.md §2): удаление запланированного спринта возвращает задачи в бэклог.
        builder.HasOne<Sprint>()
            .WithMany()
            .HasForeignKey(t => t.SprintId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => t.SprintId);

        // Ранг (§7): COLLATE "C" обязателен — по правилам локали Postgres сравнивал бы 'a' и 'B' не по ASCII,
        // и порядок ключей дробного индекса сломался бы. Unique — страховка от двух одинаковых ключей в гонке
        // (UnitOfWork переводит 23505 по нему в RankConflictException, хендлер пересчитывает ключ).
        builder.Property(t => t.Rank)
            .IsRequired()
            .HasMaxLength(200)
            .UseCollation("C");

        builder.HasIndex(t => new { t.BoardId, t.Rank })
            .IsUnique()
            .HasDatabaseName(RankIndexName);

        ConfigureChecklist(builder);
    }

    public const string RankIndexName = "IX_TaskItems_BoardId_Rank";

    /// <summary>
    /// Чек-лист (docs/TZ_task_model.md §8) — owned-коллекция, как UserLinks: своя таблица, каскад от задачи,
    /// грузится вместе с задачей (списку нужен прогресс «4/5»). Id задаёт домен — без ValueGeneratedNever
    /// EF принял бы новый пункт уже сохранённой задачи за существующий и отправил UPDATE.
    /// </summary>
    private static void ConfigureChecklist(EntityTypeBuilder<TaskItem> builder)
    {
        builder.OwnsMany(t => t.Checklist, items =>
        {
            items.ToTable("TaskChecklistItems");
            items.WithOwner().HasForeignKey("TaskId");
            items.HasKey(i => i.Id);
            items.Property(i => i.Id).ValueGeneratedNever();
            items.Property(i => i.Text).IsRequired().HasMaxLength(TaskChecklistItem.TextMaxLength);
            items.HasIndex("TaskId", nameof(TaskChecklistItem.SortOrder));
        });

        builder.Navigation(t => t.Checklist).HasField("_checklist").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
