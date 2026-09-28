// Copyright © 2026 Aiglusoft. Tous droits réservés.
// Ce fichier fait partie du patrimoine logiciel d'Aiglusoft. Toute reproduction,
// modification, distribution ou utilisation sans autorisation écrite préalable est interdite.

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aiglusoft.CsprojMigrator.Configuration;

public sealed class MigratorConfig
{
    public ProjectMatchingConfig ProjectMatching { get; set; } = new();
    public List<ProjectMappingConfig> Projects { get; set; } = new();
    public PackagesConfig Packages { get; set; } = new();
    public MigrationConfig Migration { get; set; } = new();
    public ValidationConfig Validation { get; set; } = new();
}

public sealed class ProjectMatchingConfig
{
    public List<string> FrameworkSuffixes { get; set; } = new()
    {
        "Net8", "Net7", "Net6", "Net9", "Standard", "NetStandard",
        "NetCore", "NetCore31", "NetFramework", "Framework", "Legacy",
        "Net8_0", "Net60", "Net70", "Windows", "Win"
    };
}

public sealed class ProjectMappingConfig
{
    public string Source { get; set; } = "";
    public List<ReferenceMappingConfig> References { get; set; } = new();
}

public sealed class ReferenceMappingConfig
{
    public string Project { get; set; } = "";
    public List<string> Frameworks { get; set; } = new();
}

public sealed class PackagesConfig
{
    public bool PreserveLegacyVersions { get; set; } = true;
}

public sealed class MigrationConfig
{
    public bool FailOnBlocked { get; set; } = true;
    public bool FailOnAmbiguous { get; set; } = true;
}

public sealed class ValidationConfig
{
    public bool Restore { get; set; } = true;
    public bool Build { get; set; } = true;
    public bool Test { get; set; } = true;
}

public static class ConfigLoader
{
    public static MigratorConfig Default() => new();

    public static MigratorConfig Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Default();
        if (!File.Exists(path)) return Default();
        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        try
        {
            return deserializer.Deserialize<MigratorConfig>(yaml) ?? Default();
        }
        catch
        {
            // Try JSON fallback (migration-plan.json reuse is not config, but tolerate)
            return Default();
        }
    }

    public static string ToYaml(MigrationPlanDto dto)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        return serializer.Serialize(dto);
    }
}

// Lightweight DTO for emitting an explicit-mapping skeleton from `plan`.
public sealed class MigrationPlanDto
{
    public ProjectMatchingConfig ProjectMatching { get; set; } = new();
    public List<ProjectMappingConfig> Projects { get; set; } = new();
    public PackagesConfig Packages { get; set; } = new();
    public MigrationConfig Migration { get; set; } = new();
    public ValidationConfig Validation { get; set; } = new();
}
