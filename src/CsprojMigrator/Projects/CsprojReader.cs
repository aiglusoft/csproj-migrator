// Copyright © 2026 Aiglusoft. Tous droits réservés.
// Ce fichier fait partie du patrimoine logiciel d'Aiglusoft. Toute reproduction,
// modification, distribution ou utilisation sans autorisation écrite préalable est interdite.

using System.Xml.Linq;
using Aiglusoft.CsprojMigrator.Models;

namespace Aiglusoft.CsprojMigrator.Projects;

public sealed class CsprojReader
{
    public ProjectInfo Read(string csprojPath, string? logicalNameOverride = null)
    {
        var doc = XDocument.Load(csprojPath);
        var root = doc.Root!;
        bool isSdkStyle = root.Attribute("Sdk") != null ||
            root.Elements().Any(e => e.Name.LocalName == "Sdk");

        string? Sdk = root.Attribute("Sdk")?.Value;
        var physicalName = Path.GetFileNameWithoutExtension(csprojPath);

        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pg in root.Elements().Where(e => e.Name.LocalName == "PropertyGroup"))
        {
            foreach (var p in pg.Elements())
            {
                var key = p.Name.LocalName;
                if (!props.ContainsKey(key))
                    props[key] = p.Value.Trim();
            }
        }

        props.TryGetValue("ProjectGuid", out var guid);
        props.TryGetValue("AssemblyName", out var assemblyName);
        props.TryGetValue("PackageId", out var packageId);
        props.TryGetValue("RootNamespace", out var rootNs);
        props.TryGetValue("TargetFrameworkVersion", out var tfv);

        var tfms = new List<string>();
        if (props.TryGetValue("TargetFrameworks", out var tfs) && !string.IsNullOrWhiteSpace(tfs))
            tfms.AddRange(tfs.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
        else if (props.TryGetValue("TargetFramework", out var tf) && !string.IsNullOrWhiteSpace(tf))
            tfms.Add(tf.Trim());
        else if (!string.IsNullOrWhiteSpace(tfv))
            tfms.Add(MapFrameworkVersion(tfv!));

        var projectRefs = root.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => new ProjectReferenceInfo(e.Attribute("Include")?.Value ?? ""))
            .Where(p => !string.IsNullOrWhiteSpace(p.Include))
            .ToList();

        var packageRefs = root.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => new PackageReferenceInfo(
                    e.Attribute("Include")?.Value ?? e.Attribute("Update")?.Value ?? "",
                e.Attribute("Version")?.Value ?? e.Elements().FirstOrDefault(x => x.Name.LocalName == "Version")?.Value ?? "",
                e.Attribute("Condition")?.Value))
            .Where(p => !string.IsNullOrWhiteSpace(p.Include))
            .ToList();

        // packages.config legacy
        var dir = Path.GetDirectoryName(csprojPath)!;
        bool hasPackagesConfig = File.Exists(Path.Combine(dir, "packages.config"));

        var asmRefs = root.Descendants()
            .Where(e => e.Name.LocalName == "Reference")
            .Select(e => new AssemblyReferenceInfo(
                e.Attribute("Include")?.Value ?? "",
                e.Elements().FirstOrDefault(x => x.Name.LocalName == "HintPath")?.Value))
            .Where(r => !string.IsNullOrWhiteSpace(r.Include))
            .ToList();

        var compiles = root.Descendants()
            .Where(e => e.Name.LocalName == "Compile")
            .Select(e =>
            {
                var meta = e.Attributes()
                    .Where(a => a.Name.LocalName is not ("Include" or "Update"))
                    .ToDictionary(a => a.Name.LocalName, a => a.Value);
                foreach (var child in e.Elements())
                    meta[child.Name.LocalName] = child.Value;
                IReadOnlyDictionary<string, string>? md = meta.Count == 0 ? null : meta;
                return new CompileItemInfo(
                e.Attribute("Include")?.Value ?? e.Attribute("Update")?.Value ?? "",
                    e.Elements().FirstOrDefault(x => x.Name.LocalName == "Link")?.Value,
                    e.Elements().FirstOrDefault(x => x.Name.LocalName == "DependentUpon")?.Value,
                    md);
            })
            .Where(c => !string.IsNullOrWhiteSpace(c.Include))
            .ToList();

        List<string> GetItems(string localName) => root.Descendants()
            .Where(e => e.Name.LocalName == localName)
            .Select(e => e.Attribute("Include")?.Value ?? e.Attribute("Update")?.Value ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        var imports = root.Descendants()
            .Where(e => e.Name.LocalName == "Import")
            .Select(e => e.Attribute("Project")?.Value ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        var targets = root.Descendants()
            .Where(e => e.Name.LocalName == "Target")
            .Select(e => e.Attribute("Name")?.Value ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        bool hasCustomBuild = targets.Any() ||
            root.Descendants().Any(e => e.Name.LocalName is "BeforeBuild" or "AfterBuild");

        bool hasAssemblyInfo = Directory.Exists(dir) &&
            Directory.EnumerateFiles(dir, "AssemblyInfo.cs", SearchOption.AllDirectories).Any();
        bool hasAppConfig = File.Exists(Path.Combine(dir, "App.config"));

        return new ProjectInfo
        {
            Path = System.IO.Path.GetFullPath(csprojPath),
            PhysicalName = physicalName,
            LogicalName = logicalNameOverride ?? physicalName,
            LogicalProjectId = logicalNameOverride ?? physicalName,
            ProjectGuid = string.IsNullOrWhiteSpace(guid) ? null : guid,
            AssemblyName = string.IsNullOrWhiteSpace(assemblyName) ? null : assemblyName,
            PackageId = string.IsNullOrWhiteSpace(packageId) ? null : packageId,
            RootNamespace = string.IsNullOrWhiteSpace(rootNs) ? null : rootNs,
            IsSdkStyle = isSdkStyle,
            Sdk = Sdk,
            TargetFrameworks = tfms,
            ProjectReferences = projectRefs,
            PackageReferences = packageRefs,
            AssemblyReferences = asmRefs,
            CompileItems = compiles,
            EmbeddedResources = GetItems("EmbeddedResource"),
            Contents = GetItems("Content"),
            Nones = GetItems("None"),
            Imports = imports,
            CustomTargets = targets,
            Properties = props,
            HasPackagesConfig = hasPackagesConfig,
            HasAssemblyInfo = hasAssemblyInfo,
            HasAppConfig = hasAppConfig,
            HasCustomBuildSteps = hasCustomBuild,
        };
    }

    private static string MapFrameworkVersion(string tfv)
    {
        var v = tfv.Trim().TrimStart('v', 'V');
        return v switch
        {
            "4.7.2" => "net472",
            "4.8" => "net48",
            "4.7.1" => "net471",
            "4.6.1" => "net461",
            _ => "net" + v.Replace(".", "")
        };
    }
}
