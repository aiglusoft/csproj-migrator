using CsprojMigrator.Code;
using CsprojMigrator.Configuration;
using CsprojMigrator.Dependencies;
using CsprojMigrator.Discovery;
using CsprojMigrator.Frameworks;
using CsprojMigrator.Models;
using CsprojMigrator.Packages;

namespace CsprojMigrator.Tests;

public sealed class NormalizerTests
{
    [Theory]
    [InlineData("Core", "Core")]
    [InlineData("Core.Net8", "Core")]
    [InlineData("Core.Standard", "Core")]
    [InlineData("Core.NetStandard", "Core")]
    [InlineData("Core.NetCore31", "Core")]
    [InlineData("Core.Framework", "Core")]
    [InlineData("Core.Legacy", "Core")]
    public void Normalizes_Framework_Suffixes(string input, string expected)
    {
        var n = new LogicalProjectNormalizer();
        Assert.Equal(expected, n.Normalize(input));
    }

    [Fact]
    public void Does_Not_Strip_Arbitrary_Parts()
    {
        var n = new LogicalProjectNormalizer();
        Assert.Equal("CoreReporting", n.Normalize("CoreReporting"));
    }
}

public sealed class FrameworkMergerTests
{
    [Fact]
    public void Merges_Unique_Deduped_CaseInsensitive()
    {
        var merged = TargetFrameworkMerger.Merge(
            new[] { "net472" },
            new[] { new[] { "net8.0", "NET8.0", "netstandard2.0", "netcoreapp3.1" } });
        Assert.Equal(new[] { "net472", "netstandard2.0", "netcoreapp3.1", "net8.0" }, merged);
    }

    [Fact]
    public void Only_Matched_B_Projects_Are_Merged()
    {
        // net9.0 from an unrelated project must not leak in (§21).
        var merged = TargetFrameworkMerger.Merge(
            new[] { "net472" },
            new[] { new[] { "net8.0" } });
        Assert.DoesNotContain("net9.0", merged);
    }

    [Fact]
    public void Define_Constants_Match_Spec()
    {
        Assert.Equal("NET472", TargetFrameworkParser.ToDefineConstant("net472"));
        Assert.Equal("NET8_0", TargetFrameworkParser.ToDefineConstant("net8.0"));
        Assert.Equal("NETSTANDARD2_0", TargetFrameworkParser.ToDefineConstant("netstandard2.0"));
        Assert.Equal("NETCOREAPP3_1", TargetFrameworkParser.ToDefineConstant("netcoreapp3.1"));
    }
}

public sealed class MatcherTests
{
    private static ProjectInfo P(string physical, string logical, string[]? tfms = null,
        string? guid = null, string? asm = null) => new()
        {
            Path = $"/repo/{physical}.csproj",
            PhysicalName = physical,
            LogicalName = logical,
            LogicalProjectId = logical,
            TargetFrameworks = tfms ?? Array.Empty<string>(),
            ProjectGuid = guid,
            AssemblyName = asm,
        };

    [Fact]
    public void Multiple_B_Variants_For_One_A_Is_Not_Ambiguous()
    {
        var matcher = new ProjectMatcher();
        var src = new[] { P("Core", "Core", new[] { "net472" }) };
        var refs = new[]
        {
            P("Core.Net8", "Core", new[] { "net8.0" }),
            P("Core.Standard", "Core", new[] { "netstandard2.0" }),
        };
        var mappings = matcher.Match(src, refs, new MigratorConfig());
        Assert.Single(mappings);
        Assert.False(mappings[0].IsAmbiguous);
        Assert.Equal(2, mappings[0].References.Count);
    }

    [Fact]
    public void Identical_Tfm_Variants_Are_Ambiguous()
    {
        var matcher = new ProjectMatcher();
        var src = new[] { P("Core", "Core", new[] { "net472" }) };
        var refs = new[]
        {
            P("Core.Net8", "Core", new[] { "net8.0" }),
            P("Core.Net8.Legacy", "Core", new[] { "net8.0" }),
        };
        var mappings = matcher.Match(src, refs, new MigratorConfig());
        Assert.True(mappings[0].IsAmbiguous);
    }

    [Fact]
    public void Identical_Subset_Among_Variants_Is_Ambiguous()
    {
        var matcher = new ProjectMatcher();
        var src = new[] { P("Core", "Core", new[] { "net472" }) };
        var refs = new[]
        {
            P("Core.Net8", "Core", new[] { "net8.0" }),
            P("Core.Net8.Legacy", "Core", new[] { "net8.0" }),
            P("Core.Standard", "Core", new[] { "netstandard2.0" }),
        };
        var mappings = matcher.Match(src, refs, new MigratorConfig());
        Assert.True(mappings[0].IsAmbiguous);
    }

    [Fact]
    public void Explicit_Mapping_Wins()
    {
        var matcher = new ProjectMatcher();
        var src = new[] { P("Core", "Core", new[] { "net472" }) };
        var refs = new[]
        {
            P("Core.Net8", "Core", new[] { "net8.0" }),
            P("Other", "Other", new[] { "net8.0" }),
        };
        var config = new MigratorConfig();
        config.Projects.Add(new ProjectMappingConfig
        {
            Source = "Core.csproj",
            References = new() { new ReferenceMappingConfig { Project = "Other.csproj" } },
        });
        var mappings = matcher.Match(src, refs, config);
        Assert.Equal(MappingReason.Explicit, mappings[0].Reason);
        Assert.Equal("Other", mappings[0].References[0].PhysicalName);
    }
}

public sealed class PackageConditionTests
{
    [Fact]
    public void Same_Version_Has_No_Condition()
    {
        var els = PackageConditionGenerator.ToPackageVersionElements(
            "Newtonsoft.Json",
            new Dictionary<string, string> { ["net472"] = "13.0.3", ["net8.0"] = "13.0.3" }).ToList();
        Assert.Single(els);
        Assert.Null(els[0].Attribute("Condition"));
    }

    [Fact]
    public void Different_Versions_Get_Tfm_Conditions()
    {
        var els = PackageConditionGenerator.ToPackageVersionElements(
            "Newtonsoft.Json",
            new Dictionary<string, string> { ["net472"] = "12.0.3", ["net8.0"] = "13.0.3" }).ToList();
        Assert.Equal(2, els.Count);
        Assert.All(els, e => Assert.NotNull(e.Attribute("Condition")));
    }
}

public sealed class CompatibilityTests
{
    [Fact]
    public void Per_Tfm_Blocking_Is_Detected()
    {
        var data = new ProjectInfo
        {
            Path = "/r/Data.csproj", PhysicalName = "Data", LogicalName = "Data",
            TargetFrameworks = new[] { "net472" },
        };
        var core = new ProjectInfo
        {
            Path = "/r/Core.csproj", PhysicalName = "Core", LogicalName = "Core",
            TargetFrameworks = new[] { "net472" },
            ProjectReferences = new[] { new ProjectReferenceInfo("Data.csproj") },
        };
        var analyzer = new CompatibilityAnalyzer();
        var results = analyzer.Analyze(core, new[] { "net472", "netstandard2.0" }, l =>
            l == "Data" ? data : null);
        Assert.True(results.First(r => r.Tfm == "net472").Compatible);
        Assert.False(results.First(r => r.Tfm == "netstandard2.0").Compatible);
    }
}

public sealed class CodeGenTests
{
    [Fact]
    public void Wrap_Uses_Standard_Symbols()
    {
        var wrapped = ConditionalCompilationGenerator.Wrap("Foo();", "net8.0");
        Assert.Contains("#if NET8_0", wrapped);
    }
}
