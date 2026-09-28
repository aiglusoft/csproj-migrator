using System.Text.Json;
using CsprojMigrator.Code;
using CsprojMigrator.Configuration;
using CsprojMigrator.Dependencies;
using CsprojMigrator.Discovery;
using CsprojMigrator.Frameworks;
using CsprojMigrator.Models;

namespace CsprojMigrator.Migration;

public sealed class AnalysisResult
{
    public IReadOnlyList<ProjectInfo> SourceProjects { get; init; } = Array.Empty<ProjectInfo>();
    public IReadOnlyList<ProjectInfo> ReferenceProjects { get; init; } = Array.Empty<ProjectInfo>();
    public IReadOnlyList<ProjectInfo> DestinationProjects { get; init; } = Array.Empty<ProjectInfo>();
    public IReadOnlyList<ProjectMapping> Mappings { get; init; } = Array.Empty<ProjectMapping>();
    public IReadOnlyList<string> SourceOnly { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ReferenceOnly { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DestinationTfmsByLogical { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyList<MigrationIssue> Issues { get; init; } = Array.Empty<MigrationIssue>();
    public IReadOnlyList<CodeChange> CodeChanges { get; init; } = Array.Empty<CodeChange>();
}

public sealed class MigrationPlanner
{
    private readonly ProjectDiscovery _discovery = new();
    private readonly ProjectMatcher _matcher = new();
    private readonly PackageAnalyzer _packageAnalyzer = new();
    private readonly CompatibilityAnalyzer _compat = new();
    private readonly CodeMergeEngine _codeMerge = new();

    public AnalysisResult Analyze(string source, string reference, string destination, MigratorConfig config)
    {
        var normalizer = new LogicalProjectNormalizer(config.ProjectMatching.FrameworkSuffixes);
        var srcProjects = _discovery.Discover(source, normalizer);
        var refProjects = _discovery.Discover(reference, normalizer);
        var dstProjects = Directory.Exists(destination) ? _discovery.Discover(destination, normalizer) : Array.Empty<ProjectInfo>();

        var mappings = _matcher.Match(srcProjects, refProjects, config);

        var matchedLogicals = new HashSet<string>(mappings.Where(m => m.References.Count > 0).Select(m => m.Source.LogicalName), StringComparer.OrdinalIgnoreCase);
        var refMatchedPaths = new HashSet<string>(mappings.SelectMany(m => m.References).Select(r => r.Path), StringComparer.OrdinalIgnoreCase);
        var sourceOnly = mappings.Where(m => m.References.Count == 0 && !m.IsAmbiguous).Select(m => m.Source.PhysicalName).ToList();
        var referenceOnly = refProjects.Where(r => !refMatchedPaths.Contains(r.Path)).Select(r => r.PhysicalName).ToList();

        var tfmsByLogical = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<MigrationIssue>();
        var allCodeChanges = new List<CodeChange>();

        var resolveByLogical = BuildResolver(srcProjects, refProjects);

        foreach (var m in mappings)
        {
            if (m.IsAmbiguous)
            {
                issues.Add(new MigrationIssue
                {
                    Code = "MIGRATION_AMBIGUOUS_MAPPING",
                    Severity = ChangeKind.Blocked,
                    Project = m.Source.PhysicalName,
                    Message = $"Ambiguous mapping for {m.Source.PhysicalName}: candidates {string.Join(", ", m.AmbiguousCandidates.Select(c => c.PhysicalName))}. Add explicit mapping to migration.yml.",
                });
                tfmsByLogical[m.Source.LogicalName] = TargetFrameworkParser.Order(m.Source.TargetFrameworks);
                continue;
            }

            // TFM(C) = Unique(TFM(A) ∪ TFM(all matching B)) — only matched B projects.
            var merged = TargetFrameworkMerger.Merge(
                m.Source.TargetFrameworks,
                m.References.Select(r => (IEnumerable<string>)r.TargetFrameworks));
            tfmsByLogical[m.Source.LogicalName] = merged;

            // Compatibility per TFM over source graph (destination inherits source refs).
            var compatResults = _compat.Analyze(m.Source, merged, logical =>
            {
                // Resolve within source set by logical name.
                return srcProjects.FirstOrDefault(p =>
                    string.Equals(p.LogicalName, logical, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.PhysicalName, logical, StringComparison.OrdinalIgnoreCase));
            });
            foreach (var cr in compatResults.Where(r => !r.Compatible))
            {
                issues.Add(new MigrationIssue
                {
                    Code = "MIGRATION_BLOCKED_TFM",
                    Severity = ChangeKind.Blocked,
                    Project = m.Source.PhysicalName,
                    DependencyPath = cr.BlockingPath,
                    Message = $"BLOCKED {m.Source.PhysicalName}/{cr.Tfm}: {cr.BlockingPath}",
                });
            }

            // Packages: detect legacy packages with no modern counterpart for new TFMs.
            var pkgAnalysis = _packageAnalyzer.Analyze(m.Source, m.References, merged);
            foreach (var pa in pkgAnalysis)
            {
                if (pa.ModernVersionsByTfm.Count == 0 && merged.Count > m.Source.TargetFrameworks.Count)
                {
                    issues.Add(new MigrationIssue
                    {
                        Code = "MIGRATION_PACKAGE_REVIEW",
                        Severity = ChangeKind.Review,
                        Project = m.Source.PhysicalName,
                        Message = $"Package {pa.Id} {pa.LegacyVersion} has no modern counterpart in reference; preserved for legacy TFMs, review for new TFMs." +
                                  (pa.PotentialAlternative is not null ? $" Potential alternative: {pa.PotentialAlternative} (NOT auto-installed)." : ""),
                    });
                }
            }

            // Code merge planning via Roslyn (safe: file-level diff only, no blind replace).
            try
            {
                var srcDir = Path.GetDirectoryName(m.Source.Path)!;
                var variantDirs = m.References
                    .GroupBy(r => string.Join(";", r.TargetFrameworks))
                    .Select(g => (Tfm: g.Key.Length == 0 ? "modern" : g.Key, Dir: Path.GetDirectoryName(g.First().Path)!))
                    .ToList();
                if (variantDirs.Count > 0)
                    allCodeChanges.AddRange(_codeMerge.PlanMerges(m.Source.LogicalName, srcDir, variantDirs));
            }
            catch { /* code analysis must never fail the whole analysis */ }
        }

        return new AnalysisResult
        {
            SourceProjects = srcProjects,
            ReferenceProjects = refProjects,
            DestinationProjects = dstProjects,
            Mappings = mappings,
            SourceOnly = sourceOnly,
            ReferenceOnly = referenceOnly,
            DestinationTfmsByLogical = tfmsByLogical,
            Issues = issues,
            CodeChanges = allCodeChanges,
        };
    }

    public MigrationPlan BuildPlan(AnalysisResult analysis, string source, string reference, string destination, string branch)
    {
        var projects = new List<ProjectMigration>();
        foreach (var m in analysis.Mappings)
        {
            analysis.DestinationTfmsByLogical.TryGetValue(m.Source.LogicalName, out var tfms);
            tfms ??= m.Source.TargetFrameworks;
            projects.Add(new ProjectMigration
            {
                LogicalName = m.Source.LogicalName,
                SourcePath = m.Source.Path,
                ReferencePaths = m.References.Select(r => r.Path).ToList(),
                SourceTfms = m.Source.TargetFrameworks,
                DestinationTfms = tfms,
                RequiresSdkConversion = !m.Source.IsSdkStyle && tfms.Count > 0,
                MatchReason = m.Reason,
                MatchDetail = m.ReasonDetail,
                IsSourceOnly = m.References.Count == 0 && !m.IsAmbiguous,
            });
        }

        // Aggregate package changes across projects for Central Package Management.
        var pkgById = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var pkgLegacy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in analysis.Mappings.Where(m => !m.IsAmbiguous))
        {
            var pa = _packageAnalyzer.Analyze(m.Source, m.References,
                analysis.DestinationTfmsByLogical.TryGetValue(m.Source.LogicalName, out var t) ? t : m.Source.TargetFrameworks);
            foreach (var p in pa)
            {
                if (p.LegacyVersion is not null) pkgLegacy[p.Id] = p.LegacyVersion;
                if (!pkgById.TryGetValue(p.Id, out var perTfm))
                    pkgById[p.Id] = perTfm = new(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in p.ModernVersionsByTfm)
                    perTfm[kv.Key] = kv.Value;
            }
        }
        var packageChanges = pkgById.Select(kv => new PackageChange
        {
            Id = kv.Key,
            LegacyVersion = pkgLegacy.TryGetValue(kv.Key, out var lv) ? lv : null,
            VersionsByTfm = kv.Value,
        }).ToList();

        return new MigrationPlan
        {
            SourceRepository = Path.GetFullPath(source),
            ReferenceRepository = Path.GetFullPath(reference),
            DestinationRepository = Path.GetFullPath(destination),
            Branch = branch,
            Projects = projects,
            Packages = packageChanges,
            CodeChanges = analysis.CodeChanges,
            Issues = analysis.Issues,
            SourceOnlyProjects = analysis.SourceOnly,
            ReferenceOnlyProjects = analysis.ReferenceOnly,
        };
    }

    private static Func<string, ProjectInfo?> BuildResolver(
        IReadOnlyList<ProjectInfo> src, IReadOnlyList<ProjectInfo> refs)
    {
        return logical =>
            src.FirstOrDefault(p => string.Equals(p.LogicalName, logical, StringComparison.OrdinalIgnoreCase)) ??
            refs.FirstOrDefault(p => string.Equals(p.LogicalName, logical, StringComparison.OrdinalIgnoreCase));
    }

    public static void WriteJson(string path, MigrationPlan plan)
    {
        var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static void WriteYamlSkeleton(string path, AnalysisResult analysis, MigratorConfig baseConfig)
    {
        var dto = new Configuration.MigrationPlanDto
        {
            ProjectMatching = baseConfig.ProjectMatching,
            Packages = baseConfig.Packages,
            Migration = baseConfig.Migration,
            Validation = baseConfig.Validation,
            Projects = analysis.Mappings.Where(m => m.IsAmbiguous || m.References.Count > 1).Select(m => new ProjectMappingConfig
            {
                Source = m.Source.Path,
                References = m.References.Count > 0
                    ? m.References.Select(r => new ReferenceMappingConfig
                    {
                        Project = r.Path,
                        Frameworks = r.TargetFrameworks.ToList(),
                    }).ToList()
                    : m.AmbiguousCandidates.Select(r => new ReferenceMappingConfig
                    {
                        Project = r.Path,
                        Frameworks = r.TargetFrameworks.ToList(),
                    }).ToList(),
            }).ToList(),
        };
        var serializer = new YamlDotNet.Serialization.SerializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.CamelCaseNamingConvention.Instance)
            .Build();
        File.WriteAllText(path, serializer.Serialize(dto));
    }
}
