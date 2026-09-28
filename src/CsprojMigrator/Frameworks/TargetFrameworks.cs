namespace CsprojMigrator.Frameworks;

public static class TargetFrameworkParser
{
    public static IReadOnlyList<string> Parse(string? tf, string? tfms, string? tfv)
    {
        if (!string.IsNullOrWhiteSpace(tfms))
            return tfms!.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (!string.IsNullOrWhiteSpace(tf))
            return new[] { tf!.Trim() };
        if (!string.IsNullOrWhiteSpace(tfv))
            return new[] { MapVersion(tfv!) };
        return Array.Empty<string>();
    }

    private static string MapVersion(string tfv)
    {
        var v = tfv.Trim().TrimStart('v', 'V');
        return v switch
        {
            "4.7.2" => "net472",
            "4.8" => "net48",
            _ => "net" + v.Replace(".", "")
        };
    }

    public static string Normalize(string tfm) => tfm.Trim().ToLowerInvariant();

    // Standard compile symbols per §37.
    public static string ToDefineConstant(string tfm)
    {
        var n = Normalize(tfm);
        return n switch
        {
            "net472" => "NET472",
            "net48" => "NET48",
            "net471" => "NET471",
            "net461" => "NET461",
            "net8.0" => "NET8_0",
            "net7.0" => "NET7_0",
            "net6.0" => "NET6_0",
            "net9.0" => "NET9_0",
            "netstandard2.0" => "NETSTANDARD2_0",
            "netstandard2.1" => "NETSTANDARD2_1",
            "netcoreapp3.1" => "NETCOREAPP3_1",
            _ => n.ToUpperInvariant().Replace(".", "_").Replace("-", "_")
        };
    }

    // Deterministic order: legacy net4x first, then netstandard, then netcoreapp, then modern net.
    public static IReadOnlyList<string> Order(IEnumerable<string> tfms)
    {
        int Rank(string t)
        {
            var n = Normalize(t);
            if (n.StartsWith("net4") || n is "net472" or "net48" or "net471" or "net461") return 0;
            if (n.StartsWith("netstandard")) return 1;
            if (n.StartsWith("netcoreapp")) return 2;
            return 3;
        }
        return tfms.OrderBy(Rank).ThenBy(Normalize, StringComparer.Ordinal).ToList();
    }
}

public static class TargetFrameworkMerger
{
    // TFM(C) = Unique(TFM(A) ∪ TFM(all matching B)) — §17/§55. Caller must pass
    // ONLY the B projects matched to this logical project (§21).
    public static IReadOnlyList<string> Merge(
        IEnumerable<string> sourceTfms,
        IEnumerable<IEnumerable<string>> matchedReferenceTfmSets)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in sourceTfms)
        {
            var n = TargetFrameworkParser.Normalize(t);
            if (!seen.ContainsKey(n)) seen[n] = t.Trim();
        }
        foreach (var set in matchedReferenceTfmSets)
        {
            foreach (var t in set)
            {
                var n = TargetFrameworkParser.Normalize(t);
                if (!seen.ContainsKey(n)) seen[n] = t.Trim();
            }
        }
        return TargetFrameworkParser.Order(seen.Values);
    }
}
