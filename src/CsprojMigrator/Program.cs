// Copyright © 2026 Aiglusoft. Tous droits réservés.
// Ce fichier fait partie du patrimoine logiciel d'Aiglusoft. Toute reproduction,
// modification, distribution ou utilisation sans autorisation écrite préalable est interdite.

using Aiglusoft.CsprojMigrator.Cli;
using Aiglusoft.CsprojMigrator.Configuration;
using Aiglusoft.CsprojMigrator.Discovery;
using Aiglusoft.CsprojMigrator.Migration;
using Aiglusoft.CsprojMigrator.Models;
using Aiglusoft.CsprojMigrator.Repository;
using Aiglusoft.CsprojMigrator.Validation;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!CliOptions.TryParse(args, out var opts, out var error))
        {
            if (error == "HELP") { Console.WriteLine(CliOptions.Help()); return 0; }
            Console.Error.WriteLine(error);
            Console.WriteLine(CliOptions.Help());
            return 1;
        }

        try
        {
            return opts!.Command switch
            {
                "analyze" => RunAnalyze(opts),
                "plan" => RunPlan(opts),
                "migrate" => await RunMigrateAsync(opts),
                "validate" => await RunValidateAsync(opts),
                _ => 1,
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    private static MigratorConfig LoadConfig(CliOptions opts) => ConfigLoader.Load(opts.Config);

    // Order per §44: validate A, validate B, validate C, git, discover, parse, match, ...
    private static (AnalysisResult Analysis, MigrationPlan Plan, MigratorConfig Config) Prepare(
        CliOptions opts, bool requireDestination = true)
    {
        var config = LoadConfig(opts);
        if (!Directory.Exists(opts.Source))
            throw new DirectoryNotFoundException($"Source repository not found: {opts.Source}");
        if (!Directory.Exists(opts.Reference))
            throw new DirectoryNotFoundException($"Reference repository not found: {opts.Reference}");
        if (requireDestination && !Directory.Exists(opts.Destination))
            throw new DirectoryNotFoundException($"Destination repository not found: {opts.Destination}");

        var planner = new MigrationPlanner();
        var analysis = planner.Analyze(opts.Source, opts.Reference, opts.Destination, config);
        var plan = planner.BuildPlan(analysis, opts.Source, opts.Reference, opts.Destination, opts.Branch);
        return (analysis, plan, config);
    }

    private static int RunAnalyze(CliOptions opts)
    {
        Reporter.PrintHeader(opts.Source, opts.Reference, opts.Destination);
        var (analysis, _, _) = Prepare(opts);
        if (GitRepository.IsGitRepo(opts.Destination))
            Console.WriteLine("✓ Git repositories detected");
        Reporter.PrintAnalysis(analysis);
        var blocked = analysis.Issues.Count(i => i.Severity == ChangeKind.Blocked);
        return blocked > 0 ? 2 : 0;
    }

    private static int RunPlan(CliOptions opts)
    {
        Reporter.PrintHeader(opts.Source, opts.Reference, opts.Destination);
        var (analysis, plan, config) = Prepare(opts);
        Reporter.PrintAnalysis(analysis);
        Reporter.PrintSummary(plan);

        string outPath = opts.PlanOut ??
            (opts.Config?.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) == true ||
             opts.Config?.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) == true
                ? opts.Config : "migration.yml");
        // plan command emits BOTH the deterministic JSON plan and a YAML skeleton
        // with explicit mappings for review/reuse in CI.
        MigrationPlanner.WriteJson(
            Path.ChangeExtension(outPath, ".json") is string j ? j : "migration-plan.json", plan);
        MigrationPlanner.WriteYamlSkeleton(outPath, analysis, config);
        Console.WriteLine($"Plan written: {outPath} (+ .json)");

        if (!InteractivePrompter.ResolveAmbiguities(analysis.Mappings, opts.NonInteractive, out _))
            return 2;
        return 0;
    }

    private static async Task<int> RunMigrateAsync(CliOptions opts)
    {
        Reporter.PrintHeader(opts.Source, opts.Reference, opts.Destination);
        var (analysis, plan, config) = Prepare(opts);

        // A/B read-only guard: we never write there; verify writability expectations.
        Console.WriteLine($"✓ {analysis.SourceProjects.Count} source projects found");
        Console.WriteLine($"✓ {analysis.ReferenceProjects.Count} reference projects found");

        // Ambiguities: interactive by default, hard error in CI (§7).
        if (!InteractivePrompter.ResolveAmbiguities(analysis.Mappings, opts.NonInteractive, out _))
            return 2;

        // C dirty check (§8): refuse to overwrite uncommitted changes by default.
        if (GitRepository.IsGitRepo(opts.Destination) && GitRepository.HasUncommittedChanges(opts.Destination))
        {
            Console.Error.WriteLine("Destination repository has uncommitted changes. Commit or stash before migrating.");
            return 1;
        }

        Reporter.PrintSummary(plan);
        int blockedCount = plan.Issues.Count(i => i.Severity == ChangeKind.Blocked);
        if (blockedCount > 0 && config.Migration.FailOnBlocked)
        {
            Console.Error.WriteLine($"Migration blocked by {blockedCount} BLOCKED issue(s). Resolve or set migration.failOnBlocked: false.");
            if (opts.NonInteractive) return 2;
            if (!opts.DryRun && !InteractivePrompter.Confirm("Proceed anyway?")) return 2;
        }

        if (!opts.NonInteractive && !opts.DryRun)
        {
            if (!InteractivePrompter.Confirm("Proceed with migration?")) return 0;
        }

        // Branch management (§9) — C only. A/B untouched.
        if (!opts.DryRun && GitRepository.IsGitRepo(opts.Destination))
        {
            Console.WriteLine($"Creating branch: {opts.Branch}");
            var (ok, msg) = GitRepository.CreateOrCheckoutBranch(opts.Destination, opts.Branch, opts.NonInteractive);
            if (!ok)
            {
                if (msg.StartsWith("EXISTS:"))
                {
                    if (opts.NonInteractive) { Console.Error.WriteLine(msg); return 1; }
                    var choice = InteractivePrompter.HandleExistingBranch(opts.Branch);
                    if (choice == "checkout")
                    {
                        var (ok2, msg2) = GitRepository.CheckoutBranch(opts.Destination, opts.Branch);
                        if (!ok2) { Console.Error.WriteLine(msg2); return 1; }
                    }
                    else if (choice == "new")
                    {
                        Console.Write("New branch name: ");
                        var nb = Console.ReadLine()?.Trim();
                        if (string.IsNullOrWhiteSpace(nb)) return 1;
                        plan = plan with { Branch = nb };
                        var (ok3, msg3) = GitRepository.CreateOrCheckoutBranch(opts.Destination, nb, false);
                        if (!ok3) { Console.Error.WriteLine(msg3); return 1; }
                    }
                    else return 1;
                }
                else { Console.Error.WriteLine(msg); return 1; }
            }
            Console.WriteLine("✓ Branch created");
        }

        var executor = new MigrationExecutor();
        var result = await executor.ExecuteAsync(plan, opts.DryRun,
            config.Validation.Restore, config.Validation.Build, config.Validation.Test,
            config.Migration.FailOnBlocked);
        Reporter.PrintFinal(plan, result);
        return result.Success ? 0 : 1;
    }

    private static async Task<int> RunValidateAsync(CliOptions opts)
    {
        var config = LoadConfig(opts);
        var discovery = new ProjectDiscovery();
        var normalizer = new LogicalProjectNormalizer(config.ProjectMatching.FrameworkSuffixes);
        var projects = discovery.Discover(opts.Destination, normalizer);
        var targets = projects.Select(p => (p.Path, p.TargetFrameworks)).ToList();
        var runner = new ValidationRunner(config.Validation.Restore, config.Validation.Build, config.Validation.Test);
        var results = await runner.ValidateAsync(opts.Destination, targets);
        bool ok = true;
        foreach (var g in results.GroupBy(r => (r.Project, r.Tfm)))
        {
            Console.WriteLine($"{g.Key.Project} / {g.Key.Tfm}");
            foreach (var r in g)
            {
                Console.WriteLine($"  {r.Step} {(r.Success ? "✓" : "✗")}");
                if (!r.Success)
                {
                    ok = false;
                    Console.WriteLine(r.Output);
                }
            }
        }
        return ok ? 0 : 1;
    }
}
