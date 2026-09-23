using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Flow.Application.Features.Attachments;
using Flow.Application.Features.Search;
using Flow.Shared.Contracts.About;
using MediatR;

namespace Flow.Application.Features.About.Queries.AboutQuery;

/// <summary>
/// Версию берём у входной сборки (хост Flow.Api), а не у Flow.Application: в образ они едут вместе,
/// но номер процесса — это номер хоста. SDK дописывает к InformationalVersion «+{commit}», когда сборка
/// идёт из git-клона; в образе .git нет — тогда коммита просто не будет.
/// </summary>
internal sealed class AboutQueryHandler(SearchOptions search, AttachmentOptions attachments)
    : IRequestHandler<AboutQuery, AboutResponse>
{
    private const int ShortCommitLength = 7;

    private static readonly DateTime StartedAt = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    public Task<AboutResponse> Handle(AboutQuery request, CancellationToken cancellationToken)
    {
        var (version, commit) = ParseVersion(
            (Assembly.GetEntryAssembly() ?? typeof(AboutQueryHandler).Assembly)
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

        return Task.FromResult(new AboutResponse(
            version,
            commit,
            RuntimeInformation.FrameworkDescription,
            StartedAt,
            new AboutModules(
                Search: search.Enabled,
                SmartSearch: search.Enabled && search.Embeddings.Enabled,
                ImageSearch: search.Enabled && search.VisionEnabled,
                Rerank: search.Enabled && search.RerankEnabled),
            new AboutLimits(attachments.MaxFileBytes, attachments.MaxPerTask, attachments.MaxTotalBytesPerTask)));
    }

    private static (string Version, string? Commit) ParseVersion(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational))
            return ("0.0.0", null);

        var plus = informational.IndexOf('+');
        if (plus < 0)
            return (informational, null);

        var commit = informational[(plus + 1)..];
        return (informational[..plus], commit.Length == 0 ? null : commit[..Math.Min(ShortCommitLength, commit.Length)]);
    }
}
