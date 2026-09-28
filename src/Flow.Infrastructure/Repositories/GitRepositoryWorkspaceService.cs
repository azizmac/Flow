using System.Diagnostics;
using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.Repositories;

internal sealed class GitRepositoryWorkspaceService(
    RepositoryWorkspaceOptions options,
    ILogger<GitRepositoryWorkspaceService> logger) : IRepositoryWorkspaceService
{
    public string GetAgentDirectory(CodeRepository repository)
    {
        if (repository.LastSyncedCommit is null)
            throw new InvalidOperationException("Repository has no synchronized revision.");

        return Path.Combine(options.AgentWorkspaceRoot, repository.BoardId.ToString("N"),
            repository.Id.ToString("N"), repository.LastSyncedCommit);
    }

    public Task DeleteBoardAsync(Guid boardId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.Combine(Path.GetFullPath(options.WorkspaceRoot), boardId.ToString("N"));
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        return Task.CompletedTask;
    }

    public async Task<string> SynchronizeAsync(CodeRepository repository, CancellationToken cancellationToken)
    {
        var parent = Path.Combine(Path.GetFullPath(options.WorkspaceRoot),
            repository.BoardId.ToString("N"), repository.Id.ToString("N"));
        var staging = Path.Combine(parent, ".staging-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(parent);

        foreach (var abandoned in Directory.EnumerateDirectories(parent, ".staging-*"))
            TryDelete(abandoned);

        try
        {
            await RunGitAsync(null,
                ["-c", "protocol.file.allow=never", "-c", "protocol.ext.allow=never",
                    "-c", "http.followRedirects=false", "clone", "--depth", "1", "--single-branch",
                    "--branch", repository.Branch, "--", repository.RemoteUrl, staging],
                cancellationToken);

            var commit = (await RunGitAsync(staging, ["rev-parse", "--verify", "HEAD"], cancellationToken)).Trim();
            if (commit.Length is not (40 or 64) || commit.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidOperationException("Git returned an invalid commit hash.");

            var destination = Path.Combine(parent, commit);
            if (!Directory.Exists(destination))
                Directory.Move(staging, destination);

            return commit;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to synchronize repository {RepositoryId}", repository.Id);
            throw;
        }
        finally
        {
            TryDelete(staging);
        }
    }

    private async Task<string> RunGitAsync(string? directory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        start.Environment["GIT_LFS_SKIP_SMUDGE"] = "1";

        if (directory is not null)
        {
            start.ArgumentList.Add("-C");
            start.ArgumentList.Add(directory);
        }

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.GitTimeoutSeconds)));

        using var process = new Process { StartInfo = start };
        process.Start();

        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await output;
            var stderr = await error;

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"git exited with {process.ExitCode}: {stderr}");

            return stdout;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Git operation timed out.");
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Процесс мог завершиться между проверкой HasExited и Kill.
                }

                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private void TryDelete(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not remove temporary repository directory {Path}", path);
        }
    }
}
