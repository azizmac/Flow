using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class CodeRepositoryConfiguration : IEntityTypeConfiguration<CodeRepository>
{
    public void Configure(EntityTypeBuilder<CodeRepository> builder)
    {
        builder.HasKey(repository => repository.Id);

        builder.Property(repository => repository.Id)
            .HasComment("Идентификатор подключённого Git-репозитория.");

        builder.Property(repository => repository.BoardId)
            .IsRequired()
            .HasComment("Идентификатор проекта Flow, которому принадлежит репозиторий.");

        builder.Property(repository => repository.Provider)
            .IsRequired()
            .HasComment("Git-провайдер: GitHub или GitLab.");

        builder.Property(repository => repository.Name)
            .IsRequired()
            .HasMaxLength(CodeRepository.NameMaxLength)
            .HasComment("Понятное пользователю имя репозитория.");

        builder.Property(repository => repository.RemoteUrl)
            .IsRequired()
            .HasMaxLength(CodeRepository.RemoteUrlMaxLength)
            .HasComment("HTTPS-адрес Git remote без учётных данных и параметров.");

        builder.Property(repository => repository.Branch)
            .IsRequired()
            .HasMaxLength(CodeRepository.BranchMaxLength)
            .HasComment("Ветка репозитория.");

        builder.Property(repository => repository.SyncState)
            .IsRequired()
            .HasComment("Текущее состояние синхронизации локальной копии репозитория.");

        builder.Property(repository => repository.LastSyncedCommit)
            .HasMaxLength(CodeRepository.CommitMaxLength)
            .HasComment("Commit последней успешной синхронизации; сохраняется после неудачного обновления.");

        builder.Property(repository => repository.LastSyncedAt)
            .HasComment("Время последней успешной синхронизации.");

        builder.Property(repository => repository.LastSyncError)
            .HasMaxLength(CodeRepository.SyncErrorMaxLength)
            .HasComment("Краткая безопасная причина последней неудачи синхронизации.");

        builder.Property(repository => repository.CreatedAt)
            .IsRequired()
            .HasComment("Время подключения репозитория к проекту.");

        builder.HasIndex(repository => repository.BoardId);

        // Один и тот же remote в одной доске означал бы два конкурирующих состояния синхронизации.
        builder.HasIndex(repository => new { repository.BoardId, repository.RemoteUrl }).IsUnique();
    }
}
