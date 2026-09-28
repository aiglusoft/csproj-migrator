using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using CsprojMigrator.Frameworks;
using CsprojMigrator.Models;

namespace CsprojMigrator.Code;

public sealed record SourceFileAnalysis(
    string FilePath,
    IReadOnlyList<string> ReferencedNamespaces,
    IReadOnlyList<string> InvokedSymbols,
    IReadOnlyList<string> PreprocessorSymbols,
    bool HasErrors);

public sealed class RoslynAnalyzer
{
    public SourceFileAnalysis AnalyzeFile(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetRoot();
        var namespaces = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>()
            .Select(u => u.Name?.ToString() ?? "")
            .Where(s => s.Length > 0).Distinct().ToList();
        var invocations = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Select(i => i.Expression.ToString())
            .Distinct().Take(200).ToList();
        var defines = root.DescendantTrivia()
            .Where(t => t.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IfDirectiveTrivia) ||
                        t.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ElifDirectiveTrivia))
            .Select(t => t.ToString()).ToList();
        var hasErrors = tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error);
        return new SourceFileAnalysis(filePath, namespaces, invocations, defines, hasErrors);
    }

    public IReadOnlyList<SourceFileAnalysis> AnalyzeDirectory(string projectDir)
    {
        if (!Directory.Exists(projectDir)) return Array.Empty<SourceFileAnalysis>();
        return Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/bin/") && !f.Replace('\\', '/').Contains("/obj/"))
            .Select(f => { try { return AnalyzeFile(f); } catch { return null; } })
            .Where(a => a is not null).Cast<SourceFileAnalysis>().ToList();
    }
}

public sealed class CodeMatcher
{
    // Compares A file vs B variant files with same relative name: identical -> SAFE keep;
    // different -> REVIEW (never blind text replace, §40).
    public CodeChange? Compare(string logicalName, string relativePath, string? sourceContent, IReadOnlyList<(string Tfm, string Content)> variants)
    {
        if (sourceContent is null && variants.Count == 0) return null;
        if (variants.Count == 0) return null; // A-only file: keep
        if (sourceContent is not null && variants.All(v => v.Content == sourceContent)) return null;
        if (sourceContent is null)
            return new CodeChange
            {
                ProjectLogicalName = logicalName, File = relativePath,
                Description = $"File exists only in reference variants ({string.Join(",", variants.Select(v => v.Tfm))}); manual review required before importing",
                Kind = ChangeKind.Review,
            };
        return new CodeChange
        {
            ProjectLogicalName = logicalName, File = relativePath,
            Description = $"Implementation differs between source and reference variants ({string.Join(",", variants.Select(v => v.Tfm))}); merge with #if or MSBuild condition",
            Kind = ChangeKind.Review,
            Detail = "Same type logical, different implementation between A and B",
        };
    }
}

public static class ConditionalCompilationGenerator
{
    // Wraps variant-specific snippets in #if <SYMBOL> (§37). Intra-file only;
    // whole-file differences should use MSBuild conditions (§38).
    public static string Wrap(string code, string tfm)
    {
        var symbol = TargetFrameworkParser.ToDefineConstant(tfm);
        return $"#if {symbol}\n{code}\n#endif";
    }

    public static string MergeVariants(string? legacyCode, IReadOnlyList<(string Tfm, string Code)> variants)
    {
        var sb = new System.Text.StringBuilder();
        if (legacyCode is not null)
        {
            // Legacy covers its own TFMs implicitly; keep unconditioned base.
            sb.AppendLine(legacyCode);
        }
        foreach (var (tfm, code) in variants)
        {
            sb.AppendLine(Wrap(code, tfm));
        }
        return sb.ToString();
    }

    public static string MsBuildConditionItemGroup(string tfm, string itemType, string include)
    {
        return $"<ItemGroup Condition=\"'$(TargetFramework)' == '{tfm}'\">\n  <{itemType} Include=\"{include}\" />\n</ItemGroup>";
    }
}

public sealed class CodeMergeEngine
{
    private readonly RoslynAnalyzer _analyzer = new();
    private readonly CodeMatcher _matcher = new();

    public IReadOnlyList<CodeChange> PlanMerges(
        string logicalName,
        string sourceDir,
        IReadOnlyList<(string Tfm, string Dir)> variantDirs)
    {
        var changes = new List<CodeChange>();
        var sourceFiles = SafeEnumerateCs(sourceDir);
        var allRel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in sourceFiles)
            allRel.Add(Path.GetRelativePath(sourceDir, f));
        var variantFiles = variantDirs.Select(v =>
            (v.Tfm, Files: SafeEnumerateCs(v.Dir).ToDictionary(
                f => Path.GetRelativePath(v.Dir, f), f => f,
                StringComparer.OrdinalIgnoreCase))).ToList();
        foreach (var vf in variantFiles)
            foreach (var rel in vf.Files.Keys) allRel.Add(rel);

        foreach (var rel in allRel.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var srcPath = Path.Combine(sourceDir, rel);
            string? srcContent = File.Exists(srcPath) ? File.ReadAllText(srcPath) : null;
            var variants = new List<(string Tfm, string Content)>();
            foreach (var (tfm, files) in variantFiles)
                if (files.TryGetValue(rel, out var vp))
                    variants.Add((tfm, File.ReadAllText(vp)));
            // Roslyn parse check on source (semantic sanity, §35)
            if (srcContent is not null)
            {
                try
                {
                    var tree = CSharpSyntaxTree.ParseText(srcContent);
                    if (tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
                        changes.Add(new CodeChange
                        {
                            ProjectLogicalName = logicalName, File = rel,
                            Description = "Source file has syntax errors; manual review required",
                            Kind = ChangeKind.Blocked,
                        });
                }
                catch { /* ignore */ }
            }
            var change = _matcher.Compare(logicalName, rel, srcContent, variants);
            if (change is not null) changes.Add(change);
        }
        return changes;
    }

    private static IReadOnlyList<string> SafeEnumerateCs(string dir)
    {
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        try
        {
            return Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Replace('\\', '/').Contains("/bin/") && !f.Replace('\\', '/').Contains("/obj/"))
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }
}
