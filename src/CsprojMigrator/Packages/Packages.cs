// Copyright © 2026 Aiglusoft. Tous droits réservés.
// Ce fichier fait partie du patrimoine logiciel d'Aiglusoft. Toute reproduction,
// modification, distribution ou utilisation sans autorisation écrite préalable est interdite.

using System.Xml.Linq;

namespace Aiglusoft.CsprojMigrator.Packages;

public sealed class CentralPackageManager
{
    public string BuildPropsContent(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> versionsByPackageTfm)
    {
        var project = new XElement("Project",
            new XElement("PropertyGroup",
                new XElement("ManagePackageVersionsCentrally", "true")),
            new XElement("ItemGroup",
                versionsByPackageTfm.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .SelectMany(kv => PackageConditionGenerator.ToPackageVersionElements(kv.Key, kv.Value))));
        return new XDocument(project).ToString();
    }

    public void WriteCentralProps(string directory, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> versionsByPackageTfm)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Directory.Packages.props"),
            BuildPropsContent(versionsByPackageTfm));
    }

    public static void StripVersionsFromCsproj(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);
        foreach (var el in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference").ToList())
        {
            el.Attribute("Version")?.Remove();
            foreach (var v in el.Elements().Where(e => e.Name.LocalName == "Version").ToList())
                v.Remove();
        }
        doc.Save(csprojPath);
    }
}

public static class PackageConditionGenerator
{
    // §32: conditional versions per TFM; §33: single version without condition.
    public static IEnumerable<XElement> ToPackageVersionElements(
        string packageId, IReadOnlyDictionary<string, string> versionsByTfm)
    {
        var distinct = versionsByTfm.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count <= 1)
        {
            yield return new XElement("PackageVersion",
                new XAttribute("Include", packageId),
                new XAttribute("Version", distinct.FirstOrDefault() ?? ""));
            yield break;
        }
        foreach (var kv in versionsByTfm.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            yield return new XElement("PackageVersion",
                new XAttribute("Include", packageId),
                new XAttribute("Version", kv.Value),
                new XAttribute("Condition", $"\'$(TargetFramework)\' == \'{kv.Key}\'"));
        }
    }
}
