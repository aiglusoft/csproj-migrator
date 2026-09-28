using CsprojMigrator.Models;

namespace CsprojMigrator.Discovery;

public sealed class ProjectDiscovery
{
    private readonly Projects.CsprojReader _reader = new();

    public IReadOnlyList<ProjectInfo> Discover(string repoRoot, LogicalProjectNormalizer normalizer)
    {
        if (!Directory.Exists(repoRoot)) return Array.Empty<ProjectInfo>();
        var files = Directory.EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IsIgnored(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var result = new List<ProjectInfo>();
        foreach (var f in files)
        {
            try
            {
                var physical = Path.GetFileNameWithoutExtension(f);
                var logical = normalizer.Normalize(physical);
                var info = _reader.Read(f, logical);
                // LogicalProjectId = normalized logical name
                info = info with { LogicalName = logical, LogicalProjectId = logical };
                result.Add(info);
            }
            catch
            {
                // Unparseable project: still record minimal info so it is reported, not silently dropped.
                var physical = Path.GetFileNameWithoutExtension(f);
                result.Add(new ProjectInfo
                {
                    Path = Path.GetFullPath(f),
                    PhysicalName = physical,
                    LogicalName = normalizer.Normalize(physical),
                    LogicalProjectId = normalizer.Normalize(physical),
                });
            }
        }
        return result;
    }

    private static bool IsIgnored(string path)
    {
        var lower = path.Replace('\\', '/').ToLowerInvariant();
        return lower.Contains("/bin/") || lower.Contains("/obj/") ||
               lower.Contains("/.git/") || lower.Contains("/testresults/");
    }
}
