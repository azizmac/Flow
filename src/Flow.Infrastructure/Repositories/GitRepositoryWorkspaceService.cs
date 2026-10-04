using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Flow.Application.Abstractions;
using Flow.Domain.Entities.GitIntegration;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.Repositories;

internal sealed class GitRepositoryWorkspaceService(
    RepositoryWorkspaceOptions options,
    ILogger<GitRepositoryWorkspaceService> logger) : IRepositoryWorkspaceService
{
    //TODO довольно серьёзная проверка на исключаемые директории, возможно слишком жёсткая, но пока я думаю терпимо
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".opencode", ".agent-analyses", ".env", "bin", "obj", "node_modules", ".idea", ".vs",
        ".venv", "venv", "__pycache__", ".pytest_cache", ".mypy_cache", ".tox", ".cache",
        ".next", ".nuxt", "coverage", "TestResults", "target", ".ssh", ".aws", ".azure", ".kube", ".gnupg"
    };

    //TODO довольно серьёзная проверка на исключаемые файлы, возможно слишком жёсткая, но пока я думаю терпимо
    private static readonly HashSet<string> ExcludedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "AGENTS.md", "opencode.json", "opencode.jsonc", ".git", ".opencode", ".env", ".git-credentials",
        ".npmrc", ".pypirc", ".netrc", "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519",
        "credentials", "secrets", "token", "tokens", "access_token", "refresh_token", "service-account.json"
    };

    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".key", ".pem", ".pfx", ".p12", ".der", ".crt", ".cer", ".jks", ".keystore", ".pkcs12", ".token"
    };

    private static readonly string[] ExcludedFilePrefixes =
    ["credentials.", "secrets.", "token.", "tokens.", "access_token.", "refresh_token.", "private-key.", "private_key."];

    public string GetAgentDirectory(GitRepository repository)
    {
        if (repository.LastSyncedCommit is null)
            throw new InvalidOperationException("Repository has no synchronized revision.");

        return Path.Combine(options.AgentWorkspaceRoot, repository.Id.ToString("N"), repository.LastSyncedCommit);
    }

    public async Task<string> CreateAnalysisDirectoryAsync(IReadOnlyList<GitRepository> repositories, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repositories);
        if (repositories.Count == 0)
            throw new InvalidOperationException("Нет кодовых баз для анализа.");

        var revisions = repositories.Select(repository =>
        {
            var commit = repository.LastSyncedCommit;
            if (commit is null || commit.Length is not (40 or 64) || commit.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidOperationException("У репозитория нет готовой ревизии. Сначала синхронизируйте кодовую базу.");

            return (Id: repository.Id, Commit: commit.ToLowerInvariant());
        }).OrderBy(revision => revision.Id).ToArray();

        if (revisions.Select(revision => revision.Id).Distinct().Count() != revisions.Length)
            throw new ArgumentException("Один репозиторий не может быть включён в анализ несколько раз.", nameof(repositories));

        var contextKey = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", revisions.Select(revision => $"{revision.Id:N}:{revision.Commit}")))));
        string? staging = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workspaceRoot = Path.GetFullPath(options.WorkspaceRoot);
            var analysesRoot = Path.Combine(workspaceRoot, ".agent-analyses");
            var destination = Path.Combine(analysesRoot, contextKey);
            EnsureRegularDirectory(workspaceRoot);

            foreach (var revision in revisions)
            {
                var repositoryRoot = Path.Combine(workspaceRoot, revision.Id.ToString("N"));
                var checkout = Path.Combine(repositoryRoot, revision.Commit);
                EnsureChildPath(workspaceRoot, checkout);
                EnsureRegularDirectory(repositoryRoot);
                EnsureRegularDirectory(checkout);
            }

            Directory.CreateDirectory(analysesRoot);
            EnsureRegularDirectory(analysesRoot);
            if (Directory.Exists(destination))
            {
                ValidateAnalysisDirectory(destination, revisions.Select(revision => RepositoryAlias(revision.Id)).ToArray(), cancellationToken);
                return AnalysisAgentDirectory(contextKey);
            }

            staging = Path.Combine(analysesRoot, ".staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            EnsureRegularDirectory(staging);

            foreach (var revision in revisions)
            {
                var checkout = Path.Combine(workspaceRoot, revision.Id.ToString("N"), revision.Commit);
                await CopyAnalysisDirectoryAsync(checkout, Path.Combine(staging, RepositoryAlias(revision.Id)), cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.Move(staging, destination);
            }
            catch (IOException) when (Directory.Exists(destination))
            {
                // Другой запрос мог подготовить тот же набор неизменяемых ревизий раньше нас.
                ValidateAnalysisDirectory(destination, revisions.Select(revision => RepositoryAlias(revision.Id)).ToArray(), cancellationToken);
            }

            return AnalysisAgentDirectory(contextKey);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DirectoryNotFoundException exception)
        {
            logger.LogWarning(exception, "Missing checkout while preparing agent context {ContextKey}", contextKey);
            throw new InvalidOperationException("Локальная копия кодовой базы не найдена. Синхронизируйте репозитории проекта.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to prepare agent context {ContextKey}", contextKey);
            throw new InvalidOperationException("Не удалось подготовить кодовые базы для анализа. Проверьте синхронизацию репозиториев.");
        }
        finally
        {
            if (staging is not null)
                TryDelete(staging);
        }
    }

    private string AnalysisAgentDirectory(string contextKey) =>
        Path.Combine(options.AgentWorkspaceRoot, ".agent-analyses", contextKey).Replace('\\', '/');

    private static string RepositoryAlias(Guid repositoryId) => $"repository-{repositoryId:N}";

    private static async Task CopyAnalysisDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRegularDirectory(source);
        Directory.CreateDirectory(destination);

        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsLink(entry) || (entry.Attributes & FileAttributes.Device) != 0)
                continue;

            if ((entry.Attributes & FileAttributes.Directory) != 0)
            {
                if (!IsExcludedDirectory(entry.Name))
                    await CopyAnalysisDirectoryAsync(entry.FullName, Path.Combine(destination, entry.Name), cancellationToken);
                continue;
            }

            if (IsExcludedFile(entry.Name))
                continue;

            await using var input = new FileStream(entry.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(Path.Combine(destination, entry.Name), FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private static bool IsExcludedFile(string name) =>
        ExcludedFiles.Contains(name) || ExcludedExtensions.Contains(Path.GetExtension(name)) ||
        name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) ||
        ExcludedFilePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsExcludedDirectory(string name) =>
        ExcludedDirectories.Contains(name) || name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase);

    private static void ValidateAnalysisDirectory(string path, IReadOnlyList<string> aliases, CancellationToken cancellationToken)
    {
        EnsureRegularDirectory(path);
        var entries = new DirectoryInfo(path).EnumerateFileSystemInfos().ToArray();
        if (entries.Length != aliases.Count || entries.Any(entry =>
                IsLink(entry) || (entry.Attributes & FileAttributes.Directory) == 0 || !aliases.Contains(entry.Name, StringComparer.Ordinal)))
            throw new InvalidDataException("Prepared analysis directory does not match its repository set.");

        foreach (var entry in entries)
            ValidateAnalysisContents(entry.FullName, cancellationToken);
    }

    private static void ValidateAnalysisContents(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRegularDirectory(path);
        foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsLink(entry) || (entry.Attributes & FileAttributes.Device) != 0)
                throw new InvalidDataException("Prepared analysis directory contains a non-regular entry.");

            if ((entry.Attributes & FileAttributes.Directory) != 0)
            {
                if (IsExcludedDirectory(entry.Name))
                    throw new InvalidDataException("Prepared analysis directory contains an excluded directory.");
                ValidateAnalysisContents(entry.FullName, cancellationToken);
            }
            else if (IsExcludedFile(entry.Name))
                throw new InvalidDataException("Prepared analysis directory contains an excluded file.");
        }
    }

    private static void EnsureRegularDirectory(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
            throw new DirectoryNotFoundException($"Repository directory does not exist: {path}");
        if (IsLink(directory))
            throw new InvalidDataException($"Repository directory must not be a link: {path}");
    }

    private static bool IsLink(FileSystemInfo entry) =>
        (entry.Attributes & FileAttributes.ReparsePoint) != 0 || entry.LinkTarget is not null;

    private static void EnsureChildPath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Repository path is outside the configured workspace.");
    }

    public async Task<string> SynchronizeAsync(GitRepository repository, CancellationToken cancellationToken)
    {
        var parent = Path.Combine(Path.GetFullPath(options.WorkspaceRoot),
            repository.Id.ToString("N"));
        var staging = Path.Combine(parent, ".staging-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(parent);

        foreach (var abandoned in Directory.EnumerateDirectories(parent, ".staging-*"))
            TryDelete(abandoned);

        try
        {
            await RunGitAsync(null,
                ["-c", "protocol.file.allow=never", "-c", "protocol.ext.allow=never",
                    "-c", "http.followRedirects=false", "clone", "--depth", "1", "--single-branch",
                    "--branch", repository.DefaultBranch, "--", repository.WebUrl, staging],
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
