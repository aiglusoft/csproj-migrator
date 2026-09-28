using CsprojMigrator.Models;
using CsprojMigrator.Packages;
using CsprojMigrator.Projects;
using CsprojMigrator.Repository;
using CsprojMigrator.Validation;

namespace CsprojMigrator.Migration;

public sealed record ExecutionResult(
    bool Success,
    string? ErrorCode,
    string? Message,
    IReadOnlyList<StepResult> ValidationResults,
    string ReportPath);

public sealed class MigrationExecutor
{
    private readonly CsprojWriter _writer = new();

    public async Task<ExecutionResult> ExecuteAsync(
        MigrationPlan plan,
        bool dryRun,
        bool restore, bool build, bool test,
        bool failOnBlocked)
    {
        string dest = plan.DestinationRepository;
        string? initialCommit = GitRepository.IsGitRepo(dest) ? GitRepository.CurrentHead(dest) : null;

        try
        {
            if (dryRun)
            {
                var reportPath = Path.Combine(dest, "migration-report.json");
                // Dry-run: no writes at all. Report path is informational.
                return new ExecutionResult(true, null, "Dry-run: no modifications performed.",
                    Array.Empty<StepResult>(), reportPath);
            }

            // Apply csproj changes in C. A and B are never touched (only read paths
            // in plan point to A/B; writes resolve to C by logical/relative mapping).
            var versionsByPkg = plan.Packages.ToDictionary(
                p => p.Id,
                p => (IReadOnlyDictionary<string, string>)p.VersionsByTfm,
                StringComparer.OrdinalIgnoreCase);

            if (versionsByPkg.Count > 0)
            {
                new CentralPackageManager().WriteCentralProps(dest, versionsByPkg);
            }

            var targets = new List<(string CsprojPath, IReadOnlyList<string> Tfms)>();
            foreach (var proj in plan.Projects)
            {
                var destCsproj = ResolveDestinationCsproj(dest, proj);
                if (destCsproj is null || !File.Exists(destCsproj))
                    continue; // A-only abstract entries or not yet in C: never delete/create silently.
                _writer.EnsureSdkStyle(destCsproj);
                _writer.WriteTargetFrameworks(destCsproj, proj.DestinationTfms);
                if (versionsByPkg.Count > 0)
                    CentralPackageManager.StripVersionsFromCsproj(destCsproj);
                targets.Add((destCsproj, proj.DestinationTfms));
            }

            // Validate per TFM.
            var runner = new ValidationRunner(restore, build, test);
            var results = await runner.ValidateAsync(dest, targets);

            bool buildFailed = results.Any(r => r.Step is "RESTORE" or "BUILD" && !r.Success);
            bool blocked = plan.Issues.Any(i => i.Severity == ChangeKind.Blocked);
            if (failOnBlocked && (buildFailed || blocked))
            {
                if (initialCommit is not null)
                    GitRepository.RestoreToCommit(dest, initialCommit);
                var msg = buildFailed ? "Validation failed; destination restored." : "Blocked issues present; destination restored.";
                var rp = WriteReport(plan, results, dest);
                return new ExecutionResult(false, "MIGRATION_FAILED", msg, results, rp);
            }

            var report = WriteReport(plan, results, dest);
            return new ExecutionResult(true, null, "Migration completed.", results, report);
        }
        catch (Exception ex)
        {
            if (initialCommit is not null)
            {
                try { GitRepository.RestoreToCommit(dest, initialCommit); } catch { }
            }
            return new ExecutionResult(false, "MIGRATION_FAILED", ex.Message,
                Array.Empty<StepResult>(), Path.Combine(dest, "migration-report.json"));
        }
    }

    private static string? ResolveDestinationCsproj(string destRoot, ProjectMigration proj)
    {
        // Prefer same relative path as source if it exists in C.
        // Fallback: find by file name in C.
        try
        {
            var srcFile = Path.GetFileName(proj.SourcePath);
            var direct = Directory.EnumerateFiles(destRoot, srcFile, SearchOption.AllDirectories).FirstOrDefault();
            return direct;
        }
        catch { return null; }
    }

    private static string WriteReport(MigrationPlan plan, IReadOnlyList<StepResult> results, string dest)
    {
        var path = Path.Combine(dest, "migration-report.json");
        try
        {
            ValidationRunner.WriteJsonReport(path, new
            {
                plan.SourceRepository,
                plan.ReferenceRepository,
                plan.DestinationRepository,
                plan.Branch,
                Projects = plan.Projects,
                plan.Packages,
                plan.CodeChanges,
                plan.Issues,
                Validation = results,
                GeneratedAt = DateTime.UtcNow,
            });
        }
        catch { }
        return path;
    }
}
