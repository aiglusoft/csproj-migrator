// Copyright © 2026 Aiglusoft. Tous droits réservés.
// Ce fichier fait partie du patrimoine logiciel d'Aiglusoft. Toute reproduction,
// modification, distribution ou utilisation sans autorisation écrite préalable est interdite.

using Aiglusoft.CsprojMigrator.Models;

namespace Aiglusoft.CsprojMigrator.Cli;

public static class InteractivePrompter
{
    // Resolves ambiguous mappings interactively. Returns false if user aborts.
    public static bool ResolveAmbiguities(
        IReadOnlyList<ProjectMapping> mappings, bool nonInteractive,
        out List<(ProjectMapping Mapping, ProjectInfo Chosen)> resolutions)
    {
        resolutions = new();
        var ambiguous = mappings.Where(m => m.IsAmbiguous).ToList();
        if (ambiguous.Count == 0) return true;
        if (nonInteractive)
        {
            foreach (var m in ambiguous)
            {
                Console.Error.WriteLine("ERROR MIGRATION_AMBIGUOUS_MAPPING");
                Console.Error.WriteLine();
                Console.Error.WriteLine($"Project:\n  {m.Source.PhysicalName}");
                Console.Error.WriteLine("Candidates:");
                foreach (var c in m.AmbiguousCandidates)
                    Console.Error.WriteLine($"  {c.PhysicalName}");
                Console.Error.WriteLine();
                Console.Error.WriteLine("No deterministic mapping found.");
                Console.Error.WriteLine("Resolution:\n  Add an explicit mapping to migration.yml.");
            }
            return false;
        }

        foreach (var m in ambiguous)
        {
            Console.WriteLine();
            Console.WriteLine($"Ambiguous mapping");
            Console.WriteLine($"Project: {m.Source.PhysicalName}");
            Console.WriteLine($"Reason: {m.ReasonDetail}");
            Console.WriteLine("Which project should be used?");
            for (int i = 0; i < m.AmbiguousCandidates.Count; i++)
            {
                var c = m.AmbiguousCandidates[i];
                Console.WriteLine($"  {i + 1}. {c.PhysicalName} (TFMs: {string.Join(";", c.TargetFrameworks)})");
            }
            Console.WriteLine($"  {m.AmbiguousCandidates.Count + 1}. Define explicit mapping later (skip)");
            Console.WriteLine($"  {m.AmbiguousCandidates.Count + 2}. Abort");
            Console.Write("Select: ");
            var input = Console.ReadLine()?.Trim();
            if (int.TryParse(input, out int choice))
            {
                if (choice >= 1 && choice <= m.AmbiguousCandidates.Count)
                {
                    resolutions.Add((m, m.AmbiguousCandidates[choice - 1]));
                    continue;
                }
                if (choice == m.AmbiguousCandidates.Count + 1) continue;
            }
            return false;
        }
        return true;
    }

    public static bool Confirm(string message)
    {
        Console.Write($"{message} [y/N]: ");
        var input = Console.ReadLine()?.Trim().ToLowerInvariant();
        return input is "y" or "yes";
    }

    public static string HandleExistingBranch(string branch)
    {
        Console.WriteLine($"Branch already exists: {branch}");
        Console.WriteLine("Options:");
        Console.WriteLine("  1. Abort");
        Console.WriteLine("  2. Checkout existing branch");
        Console.WriteLine("  3. Use another branch");
        Console.Write("Select: ");
        var input = Console.ReadLine()?.Trim();
        return input switch
        {
            "2" => "checkout",
            "3" => "new",
            _ => "abort",
        };
    }
}

public static class Reporter
{
    public static void PrintHeader(string source, string reference, string destination)
    {
        Console.WriteLine("CSProj Migrator");
        Console.WriteLine("────────────────────────────────────────");
        Console.WriteLine($"Source       : {source}");
        Console.WriteLine($"Reference    : {reference}");
        Console.WriteLine($"Destination  : {destination}");
        Console.WriteLine();
    }

    public static void PrintAnalysis(Migration.AnalysisResult a)
    {
        Console.WriteLine($"✓ {a.SourceProjects.Count} source projects found");
        Console.WriteLine($"✓ {a.ReferenceProjects.Count} reference projects found");
        var matched = a.Mappings.Count(m => !m.IsAmbiguous && m.References.Count > 0);
        Console.WriteLine($"✓ {matched} projects matched automatically");
        var amb = a.Mappings.Count(m => m.IsAmbiguous);
        if (amb > 0) Console.WriteLine($"⚠ {amb} ambiguous project mapping(s)");
        foreach (var m in a.Mappings)
        {
            if (m.IsAmbiguous)
            {
                Console.WriteLine($"  AMBIGUOUS {m.Source.PhysicalName}: {string.Join(", ", m.AmbiguousCandidates.Select(c => c.PhysicalName))}");
            }
        }
        if (a.SourceOnly.Count > 0)
        {
            Console.WriteLine("INFO source-only projects (kept unchanged):");
            foreach (var s in a.SourceOnly) Console.WriteLine($"  {s}");
        }
        if (a.ReferenceOnly.Count > 0)
        {
            Console.WriteLine("INFO reference-only projects (not auto-imported):");
            foreach (var s in a.ReferenceOnly) Console.WriteLine($"  {s}");
        }
        Console.WriteLine();
        Console.WriteLine("PROJECT MATCHING");
        Console.WriteLine("────────────────────────────────");
        foreach (var m in a.Mappings)
        {
            Console.WriteLine($"{m.Source.PhysicalName} (Logical: {m.Source.LogicalName})");
            Console.WriteLine($"  Reason: {m.Reason} — {m.ReasonDetail}");
            foreach (var r in m.References)
                Console.WriteLine($"    → {r.PhysicalName} [{string.Join(";", r.TargetFrameworks)}]");
            if (a.DestinationTfmsByLogical.TryGetValue(m.Source.LogicalName, out var tfms))
                Console.WriteLine($"  Destination TFMs: {string.Join(";", tfms)}");
        }
        if (a.Issues.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("ISSUES");
            Console.WriteLine("────────────────────────────────");
            foreach (var i in a.Issues)
                Console.WriteLine($"[{i.Severity}] {i.Code} {i.Project}: {i.Message}");
        }
    }

    public static void PrintSummary(Models.MigrationPlan plan)
    {
        int toConvert = plan.Projects.Count(p => p.RequiresSdkConversion);
        int multi = plan.Projects.Count(p => p.DestinationTfms.Count > 1);
        int safe = plan.CodeChanges.Count(c => c.Kind == ChangeKind.Safe);
        int review = plan.CodeChanges.Count(c => c.Kind == ChangeKind.Review);
        int blocked = plan.CodeChanges.Count(c => c.Kind == ChangeKind.Blocked) +
                      plan.Issues.Count(i => i.Severity == ChangeKind.Blocked);
        Console.WriteLine();
        Console.WriteLine("Migration summary");
        Console.WriteLine("────────────────────────────────");
        Console.WriteLine($"Projects: {plan.Projects.Count}");
        Console.WriteLine($"To convert to SDK-style: {toConvert}");
        Console.WriteLine($"Multi-target projects: {multi}");
        Console.WriteLine($"Packages: {plan.Packages.Count}");
        Console.WriteLine($"Code changes: SAFE {safe}  REVIEW {review}  BLOCKED {blocked}");
    }

    public static void PrintFinal(Models.MigrationPlan plan, Migration.ExecutionResult exec)
    {
        Console.WriteLine("CSProj Migrator");
        Console.WriteLine("══════════════════════════════════════");
        Console.WriteLine(exec.Success ? "Migration completed." : "Migration failed.");
        Console.WriteLine($"Destination: {plan.DestinationRepository}");
        Console.WriteLine($"Branch: {plan.Branch}");
        Console.WriteLine($"Projects: {plan.Projects.Count} migrated, {plan.SourceOnlyProjects.Count} kept unchanged");
        Console.WriteLine($"Code changes: REVIEW {plan.CodeChanges.Count(c => c.Kind == ChangeKind.Review)}  BLOCKED {plan.CodeChanges.Count(c => c.Kind == ChangeKind.Blocked)}");
        Console.WriteLine($"Report: {exec.ReportPath}");
        if (exec.ValidationResults.Count > 0)
        {
            foreach (var g in exec.ValidationResults.GroupBy(r => r.Project))
            {
                Console.WriteLine($"{g.Key}");
                foreach (var r in g)
                    Console.WriteLine($"  {r.Tfm} {r.Step} {(r.Success ? "✓" : "✗")}");
            }
        }
    }
}
