using CsprojMigrator.Models;

namespace CsprojMigrator.Dependencies;

public sealed record CompatibilityResult(
    string Tfm,
    bool Compatible,
    string? BlockingPath,
    string? Message);

public sealed class ProjectGraphBuilder
{
    // Builds logical-name graph: logical -> referenced logical names (resolved via path).
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Build(
        IReadOnlyList<ProjectInfo> projects, string repoRoot)
    {
        var byFileName = projects
            .GroupBy(p => Path.GetFileName(p.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var byFull = projects.ToDictionary(p => p.Path, p => p, StringComparer.OrdinalIgnoreCase);

        var graph = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projects)
        {
            var refs = new List<string>();
            foreach (var r in p.ProjectReferences)
            {
                var resolved = Resolve(r.Include, Path.GetDirectoryName(p.Path)!);
                if (byFull.TryGetValue(resolved, out var target))
                    refs.Add(target.LogicalName);
                else if (byFileName.TryGetValue(Path.GetFileName(resolved), out var t2))
                    refs.Add(t2.LogicalName);
                else
                    refs.Add(Path.GetFileNameWithoutExtension(resolved));
            }
            if (!graph.ContainsKey(p.LogicalName))
                graph[p.LogicalName] = refs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        return graph;
    }

    private static string Resolve(string include, string baseDir)
    {
        try
        {
            var combined = Path.GetFullPath(Path.Combine(baseDir, include.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
            return combined;
        }
        catch { return include; }
    }
}

public sealed class CompatibilityAnalyzer
{
    // Per-TFM check (§23): for each TFM of `project`, every referenced project must support it.
    public IReadOnlyList<CompatibilityResult> Analyze(
        ProjectInfo project,
        IReadOnlyList<string> destinationTfms,
        Func<string, ProjectInfo?> resolveLogical)
    {
        var results = new List<CompatibilityResult>();
        foreach (var tfm in destinationTfms)
        {
            var blocked = FindBlockingPath(project, Frameworks.TargetFrameworkParser.Normalize(tfm), resolveLogical, new Stack<string>());
            if (blocked is null)
                results.Add(new CompatibilityResult(tfm, true, null, null));
            else
                results.Add(new CompatibilityResult(tfm, false, blocked,
                    $"{project.LogicalName}/{tfm} blocked: {blocked}"));
        }
        return results;
    }

    private static string? FindBlockingPath(ProjectInfo project, string tfmNorm,
        Func<string, ProjectInfo?> resolve, Stack<string> chain)
    {
        chain.Push(project.LogicalName);
        try
        {
            foreach (var pref in project.ProjectReferences)
            {
                var logical = GuessLogical(pref, project);
                var target = resolve(logical);
                if (target is null) continue; // external / unknown: cannot prove incompatible
                bool supports = target.TargetFrameworks.Any(t =>
                    Frameworks.TargetFrameworkParser.Normalize(t) == tfmNorm);
                if (!supports)
                {
                    var path = string.Join(" -> ", chain.Reverse().Concat(new[] { $"{target.LogicalName} ({target.PhysicalName}: {string.Join(",", target.TargetFrameworks)})" }));
                    return $"{path} does not support {tfmNorm}";
                }
                var deeper = FindBlockingPath(target, tfmNorm, resolve, chain);
                if (deeper is not null) return deeper;
            }
            return null;
        }
        finally { chain.Pop(); }
    }

    private static string GuessLogical(ProjectReferenceInfo pref, ProjectInfo from)
    {
        try
        {
            var combined = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(from.Path)!, pref.Include));
            return Path.GetFileNameWithoutExtension(combined);
        }
        catch { return pref.Include; }
    }
}

public sealed class PackageAnalyzer
{
    public sealed record PackageAnalysis(
        string Id,
        string? LegacyVersion,
        IReadOnlyDictionary<string, string> ModernVersionsByTfm,
        bool LegacyIncompatibleWithSomeTfm,
        string? PotentialAlternative);

    // Groups package versions: legacy (A) is source of truth for legacy TFMs (§30);
    // modern (B) versions apply to new TFMs (§32). Never auto-upgrades legacy (§53 R6).
    public IReadOnlyList<PackageAnalysis> Analyze(
        ProjectInfo source,
        IReadOnlyList<ProjectInfo> matchedReferences,
        IReadOnlyList<string> destinationTfms)
    {
        var legacy = source.PackageReferences
            .GroupBy(p => p.Include, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Version, StringComparer.OrdinalIgnoreCase);

        // modern version per package: prefer version from the B variant whose TFMs cover the TFM
        var modernByPkgTfm = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in matchedReferences)
        {
            foreach (var pkg in b.PackageReferences)
            {
                if (!modernByPkgTfm.TryGetValue(pkg.Include, out var perTfm))
                    modernByPkgTfm[pkg.Include] = perTfm = new();
                foreach (var tfm in b.TargetFrameworks)
                {
                    var n = Frameworks.TargetFrameworkParser.Normalize(tfm);
                    if (!perTfm.ContainsKey(n) && !string.IsNullOrWhiteSpace(pkg.Version))
                        perTfm[n] = pkg.Version;
                }
            }
        }

        var ids = legacy.Keys.Union(modernByPkgTfm.Keys, StringComparer.OrdinalIgnoreCase);
        var result = new List<PackageAnalysis>();
        foreach (var id in ids)
        {
            legacy.TryGetValue(id, out var legacyVersion);
            modernByPkgTfm.TryGetValue(id, out var modernMap);
            modernMap ??= new();

            var versionsByTfm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tfm in destinationTfms)
            {
                var n = Frameworks.TargetFrameworkParser.Normalize(tfm);
                bool isLegacyTfm = source.TargetFrameworks.Any(t =>
                    Frameworks.TargetFrameworkParser.Normalize(t) == n);
                if (isLegacyTfm && !string.IsNullOrWhiteSpace(legacyVersion))
                    versionsByTfm[tfm] = legacyVersion!;
                else if (modernMap.TryGetValue(n, out var mv))
                    versionsByTfm[tfm] = mv;
                else if (!string.IsNullOrWhiteSpace(legacyVersion))
                    versionsByTfm[tfm] = legacyVersion!;
            }

            // Heuristic: legacy-only packages with no modern counterpart and names
            // suggesting legacy DB/client stacks are flagged REVIEW/BLOCKED downstream.
            string? alt = null;
            if (modernMap.Count == 0 && !string.IsNullOrWhiteSpace(legacyVersion))
            {
                foreach (var b in matchedReferences)
                {
                    var candidate = b.PackageReferences.FirstOrDefault(p =>
                        !string.Equals(p.Include, id, StringComparison.OrdinalIgnoreCase) &&
                        SharesToken(p.Include, id));
                    if (candidate is not null) { alt = candidate.Include; break; }
                }
            }

            result.Add(new PackageAnalysis(id, legacyVersion, versionsByTfm, false, alt));
        }
        return result;
    }

    private static bool SharesToken(string a, string b)
    {
        var ta = a.Split('.', '-', '_');
        var tb = new HashSet<string>(b.Split('.', '-', '_'), StringComparer.OrdinalIgnoreCase);
        return ta.Any(t => t.Length > 3 && tb.Contains(t));
    }
}
