using CsprojMigrator.Configuration;
using CsprojMigrator.Models;

namespace CsprojMigrator.Discovery;

public sealed class ProjectMatcher
{
    public IReadOnlyList<ProjectMapping> Match(
        IReadOnlyList<ProjectInfo> source,
        IReadOnlyList<ProjectInfo> reference,
        MigratorConfig config)
    {
        var explicitMap = BuildExplicitIndex(config);
        var byLogical = reference.GroupBy(r => r.LogicalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var result = new List<ProjectMapping>();
        foreach (var s in source)
        {
            // 1. explicit mapping (match by relative or absolute source path suffix)
            if (TryExplicit(s, reference, explicitMap, out var explicitRefs))
            {
                result.Add(new ProjectMapping
                {
                    Source = s,
                    References = explicitRefs!,
                    Reason = MappingReason.Explicit,
                    ReasonDetail = $"Explicit mapping in config for {s.PhysicalName}",
                });
                continue;
            }

            // 2. normalized logical name
            if (byLogical.TryGetValue(s.LogicalName, out var logicalCandidates) && logicalCandidates.Count > 0)
            {
                if (logicalCandidates.Count == 1)
                {
                    result.Add(new ProjectMapping
                    {
                        Source = s,
                        References = logicalCandidates,
                        Reason = MappingReason.LogicalName,
                        ReasonDetail = $"LogicalName = {s.LogicalName}",
                    });
                }
                else
                {
                    // Multiple B variants for one A is NORMAL (§14) — not ambiguous
                    // unless they expose identical TFMs with no distinguishing metadata.
                    if (AreVariantsDistinguishable(logicalCandidates))
                    {
                        result.Add(new ProjectMapping
                        {
                            Source = s,
                            References = logicalCandidates,
                            Reason = MappingReason.LogicalName,
                            ReasonDetail = $"LogicalName = {s.LogicalName} ({logicalCandidates.Count} variants)",
                        });
                    }
                    else
                    {
                        result.Add(new ProjectMapping
                        {
                            Source = s,
                            References = Array.Empty<ProjectInfo>(),
                            Reason = MappingReason.LogicalName,
                            ReasonDetail = $"LogicalName = {s.LogicalName} but candidates indistinguishable",
                            IsAmbiguous = true,
                            AmbiguousCandidates = logicalCandidates,
                        });
                    }
                }
                continue;
            }

            // 3-9. fallback heuristics in priority order
            var scored = new List<(ProjectInfo Ref, MappingReason Reason, string Detail)>();
            foreach (var r in reference)
            {
                if (!string.IsNullOrWhiteSpace(s.ProjectGuid) && string.Equals(s.ProjectGuid, r.ProjectGuid, StringComparison.OrdinalIgnoreCase))
                    scored.Add((r, MappingReason.ProjectGuid, $"ProjectGuid = {s.ProjectGuid}"));
                else if (!string.IsNullOrWhiteSpace(s.AssemblyName) && string.Equals(s.AssemblyName, r.AssemblyName, StringComparison.OrdinalIgnoreCase))
                    scored.Add((r, MappingReason.AssemblyName, $"AssemblyName = {s.AssemblyName}"));
                else if (!string.IsNullOrWhiteSpace(s.PackageId) && string.Equals(s.PackageId, r.PackageId, StringComparison.OrdinalIgnoreCase))
                    scored.Add((r, MappingReason.PackageId, $"PackageId = {s.PackageId}"));
                else if (!string.IsNullOrWhiteSpace(s.RootNamespace) && string.Equals(s.RootNamespace, r.RootNamespace, StringComparison.OrdinalIgnoreCase))
                    scored.Add((r, MappingReason.RootNamespace, $"RootNamespace = {s.RootNamespace}"));
                else if (Path.GetFileName(s.Path).Equals(Path.GetFileName(r.Path), StringComparison.OrdinalIgnoreCase))
                    scored.Add((r, MappingReason.Path, $"Path file name = {Path.GetFileName(s.Path)}"));
            }

            if (scored.Count == 1)
            {
                result.Add(new ProjectMapping
                {
                    Source = s,
                    References = new[] { scored[0].Ref },
                    Reason = scored[0].Reason,
                    ReasonDetail = scored[0].Detail,
                });
            }
            else if (scored.Count > 1)
            {
                // Prefer best priority (lowest enum value); if tie -> ambiguous
                var best = scored.MinBy(x => (int)x.Reason);
                var tied = scored.Where(x => x.Reason == best.Reason).ToList();
                if (tied.Count == 1)
                {
                    result.Add(new ProjectMapping
                    {
                        Source = s, References = new[] { best.Ref },
                        Reason = best.Reason, ReasonDetail = best.Detail,
                    });
                }
                else
                {
                    result.Add(new ProjectMapping
                    {
                        Source = s,
                        References = Array.Empty<ProjectInfo>(),
                        Reason = best.Reason,
                        ReasonDetail = "Multiple candidates, no deterministic winner",
                        IsAmbiguous = true,
                        AmbiguousCandidates = tied.Select(t => t.Ref).ToList(),
                    });
                }
            }
            else
            {
                result.Add(new ProjectMapping
                {
                    Source = s,
                    References = Array.Empty<ProjectInfo>(),
                    Reason = MappingReason.Unmatched,
                    ReasonDetail = "No reference candidate (source-only project, kept unchanged)",
                });
            }
        }
        return result;
    }

    private static bool AreVariantsDistinguishable(IReadOnlyList<ProjectInfo> candidates)
    {
        // Distinguishable only if EVERY candidate is uniquely identified by its
        // (TFM set, AssemblyName, PackageId) signature (§16): two variants
        // exposing the same TFM with no other distinguishing metadata are ambiguous,
        // even when a third variant differs.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates)
        {
            var key = string.Join(";", c.TargetFrameworks
                    .Select(Frameworks.TargetFrameworkParser.Normalize)
                    .OrderBy(x => x, StringComparer.Ordinal))
                + "|" + (c.AssemblyName ?? "") + "|" + (c.PackageId ?? "");
            if (!seen.Add(key)) return false;
        }
        return true;
    }

    private static Dictionary<string, List<string>> BuildExplicitIndex(MigratorConfig config)
    {
        var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in config.Projects)
            dict[NormalizePath(p.Source)] = p.References.Select(r => NormalizePath(r.Project)).ToList();
        return dict;
    }

    private static string NormalizePath(string p) => p.Replace('\\', '/').Trim().TrimStart('.', '/');

    private static bool TryExplicit(ProjectInfo s, IReadOnlyList<ProjectInfo> reference,
        Dictionary<string, List<string>> index, out IReadOnlyList<ProjectInfo>? refs)
    {
        refs = null;
        var full = s.Path.Replace('\\', '/');
        var hit = index.FirstOrDefault(kv =>
            full.EndsWith(kv.Key, StringComparison.OrdinalIgnoreCase) ||
            full.EndsWith(kv.Key.TrimStart('/'), StringComparison.OrdinalIgnoreCase));
        if (hit.Key is null) return false;
        var matched = new List<ProjectInfo>();
        foreach (var rp in hit.Value)
        {
            var m = reference.FirstOrDefault(r =>
                r.Path.Replace('\\', '/').EndsWith(rp, StringComparison.OrdinalIgnoreCase));
            if (m is not null) matched.Add(m);
        }
        refs = matched;
        return true;
    }
}
