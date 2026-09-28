using System.Xml.Linq;
using CsprojMigrator.Models;

namespace CsprojMigrator.Projects;

public sealed class SdkConverter
{
    public sealed record ConversionAssessment(
        bool CanConvert,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> Blockers);

    // Pre-conversion analysis (§28): never silently drop complex constructs.
    public ConversionAssessment Assess(ProjectInfo project)
    {
        var warnings = new List<string>();
        var blockers = new List<string>();
        if (project.Imports.Any()) warnings.Add($"Custom imports: {string.Join(", ", project.Imports)}");
        if (project.CustomTargets.Any()) warnings.Add($"Custom targets: {string.Join(", ", project.CustomTargets)}");
        if (project.HasCustomBuildSteps) warnings.Add("Custom BeforeBuild/AfterBuild steps present");
        if (project.HasPackagesConfig) warnings.Add("packages.config must be migrated to PackageReference");
        if (project.AssemblyReferences.Any()) warnings.Add($"{project.AssemblyReferences.Count} legacy Reference/HintPath entries require review");
        if (project.CompileItems.Any(c => c.Metadata is not null && c.Metadata.Count > 0))
            warnings.Add("Compile items with metadata (Link/DependentUpon/Generator/...) require REVIEW before removal");
        if (project.Properties.Any(kv => kv.Key.StartsWith("OutputPath", StringComparison.OrdinalIgnoreCase)))
            warnings.Add("Custom OutputPath properties present");
        return new ConversionAssessment(blockers.Count == 0, warnings, blockers);
    }

    public bool NeedsConversion(ProjectInfo project, IReadOnlyList<string> destinationTfms) =>
        !project.IsSdkStyle && destinationTfms.Count > 0;

    // Generates minimal SDK-style csproj content. Callers decide whether to write.
    public string GenerateSdkStyle(ProjectInfo project, IReadOnlyList<string> destinationTfms)
    {
        var tfms = string.Join(";", destinationTfms);
        var doc = new XDocument(
            new XElement("Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup",
                    new XElement("TargetFrameworks", tfms),
                    string.IsNullOrWhiteSpace(project.AssemblyName) ? null : new XElement("AssemblyName", project.AssemblyName),
                    string.IsNullOrWhiteSpace(project.RootNamespace) ? null : new XElement("RootNamespace", project.RootNamespace),
                    string.IsNullOrWhiteSpace(project.PackageId) ? null : new XElement("PackageId", project.PackageId)
                )));
        return doc.ToString();
    }
}

public sealed class CsprojWriter
{
    public void WriteTargetFrameworks(string csprojPath, IReadOnlyList<string> tfms)
    {
        var doc = XDocument.Load(csprojPath);
        var root = doc.Root!;
        var ns = root.Name.Namespace;
        XName Pg(string n) => ns == XNamespace.None ? n : ns + n;

        // Remove legacy single-TFM elements, then ensure one TargetFrameworks.
        foreach (var pg in root.Elements(Pg("PropertyGroup")).ToList())
        {
            foreach (var el in pg.Elements().Where(e =>
                e.Name.LocalName is "TargetFramework" or "TargetFrameworks" or "TargetFrameworkVersion").ToList())
                el.Remove();
            if (!pg.Elements().Any()) pg.Remove();
        }
        var group = new XElement(Pg("PropertyGroup"),
            new XElement(Pg("TargetFrameworks"), string.Join(";", tfms)));
        root.AddFirst(group);
        doc.Save(csprojPath);
    }

    public void EnsureSdkStyle(string csprojPath, string sdk = "Microsoft.NET.Sdk")
    {
        var doc = XDocument.Load(csprojPath);
        var root = doc.Root!;
        if (root.Attribute("Sdk") is null)
            root.SetAttributeValue("Sdk", sdk);
        // Remove explicit Compile Includes that are now implicit AND have no special metadata (§29).
        foreach (var el in root.Descendants().Where(e => e.Name.LocalName == "Compile").ToList())
        {
            bool hasSpecial = el.Attributes().Any(a => a.Name.LocalName is not ("Include" or "Update")) || el.Elements().Any();
            var inc = el.Attribute("Include")?.Value ?? el.Attribute("Update")?.Value ?? "";
            if (!hasSpecial && inc.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                el.Remove();
        }
        // Prune now-empty ItemGroups.
        foreach (var ig in root.Descendants().Where(e => e.Name.LocalName == "ItemGroup").ToList())
            if (!ig.Elements().Any() && !ig.Attributes().Any()) ig.Remove();
        doc.Save(csprojPath);
    }
}
