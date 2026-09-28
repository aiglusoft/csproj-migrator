namespace CsprojMigrator.Discovery;

public sealed class LogicalProjectNormalizer
{
    private readonly IReadOnlyList<string> _suffixes;

    public LogicalProjectNormalizer(IEnumerable<string>? suffixes = null)
    {
        _suffixes = (suffixes ?? DefaultSuffixes()).ToList();
    }

    public static IReadOnlyList<string> DefaultSuffixes() => new[]
    {
        "Net8", "Net7", "Net6", "Net9", "Standard", "NetStandard",
        "NetCore", "NetCore31", "NetFramework", "Framework", "Legacy",
        "Net80", "Net70", "Net60", "Net472", "Net48", "Core", "Modern"
    };

    // "Core.Net8" -> "Core", "Core.Standard" -> "Core".
    // Only strips suffix segments after '.' or '-' or trailing suffix.
    public string Normalize(string physicalName)
    {
        if (string.IsNullOrWhiteSpace(physicalName)) return physicalName;
        var name = physicalName.Trim();
        // Strip separators-based suffix: split on '.' and '-' and drop trailing
        // segments that match known suffixes (case-insensitive).
        var separators = new[] { '.', '-' };
        string[] parts = name.Split(separators);
        if (parts.Length > 1)
        {
            var kept = parts.ToList();
            while (kept.Count > 1 && IsKnownSuffix(kept[^1]))
                kept.RemoveAt(kept.Count - 1);
            if (kept.Count != parts.Length)
                return string.Join(".", kept);
        }
        // Trailing suffix without separator: "CoreNet8" -> "Core"
        foreach (var suffix in _suffixes.OrderByDescending(s => s.Length))
        {
            if (name.Length > suffix.Length &&
                name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var stem = name[..^suffix.Length].TrimEnd('.', '-', '_');
                if (stem.Length >= 2)
                    return stem;
            }
        }
        return name;
    }

    private bool IsKnownSuffix(string segment)
    {
        var compact = segment.Replace("_", "").Replace(" ", "");
        return _suffixes.Any(s =>
            string.Equals(compact, s, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(compact, s.Replace("_", ""), StringComparison.OrdinalIgnoreCase));
    }
}
