namespace CsprojMigrator.Models;

public sealed record ProjectReferenceInfo(string Include, string ResolvedPath = "");

public sealed record PackageReferenceInfo(string Include, string Version, string? Condition = null);

public sealed record AssemblyReferenceInfo(string Include, string? HintPath = null);

public sealed record CompileItemInfo(string Include, string? Link = null, string? DependentUpon = null, IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record ProjectInfo
{
    public string Path { get; init; } = "";
    public string PhysicalName { get; init; } = "";
    public string LogicalName { get; init; } = "";
    public string LogicalProjectId { get; init; } = "";
    public string? ProjectGuid { get; init; }
    public string? AssemblyName { get; init; }
    public string? PackageId { get; init; }
    public string? RootNamespace { get; init; }
    public bool IsSdkStyle { get; init; }
    public string? Sdk { get; init; }
    public IReadOnlyList<string> TargetFrameworks { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ProjectReferenceInfo> ProjectReferences { get; init; } = Array.Empty<ProjectReferenceInfo>();
    public IReadOnlyList<PackageReferenceInfo> PackageReferences { get; init; } = Array.Empty<PackageReferenceInfo>();
    public IReadOnlyList<AssemblyReferenceInfo> AssemblyReferences { get; init; } = Array.Empty<AssemblyReferenceInfo>();
    public IReadOnlyList<CompileItemInfo> CompileItems { get; init; } = Array.Empty<CompileItemInfo>();
    public IReadOnlyList<string> EmbeddedResources { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Contents { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Nones { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Imports { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CustomTargets { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
    public bool HasPackagesConfig { get; init; }
    public bool HasAssemblyInfo { get; init; }
    public bool HasAppConfig { get; init; }
    public bool HasCustomBuildSteps { get; init; }
}

public enum MappingReason
{
    Explicit,
    LogicalName,
    ProjectGuid,
    AssemblyName,
    PackageId,
    RootNamespace,
    Path,
    DependencyGraph,
    MsBuildMetadata,
    Unmatched
}

public sealed record ProjectMapping
{
    public ProjectInfo Source { get; init; } = null!;
    public IReadOnlyList<ProjectInfo> References { get; init; } = Array.Empty<ProjectInfo>();
    public MappingReason Reason { get; init; } = MappingReason.Unmatched;
    public string ReasonDetail { get; init; } = "";
    public bool IsAmbiguous { get; init; }
    public IReadOnlyList<ProjectInfo> AmbiguousCandidates { get; init; } = Array.Empty<ProjectInfo>();
}

public enum ChangeKind { Safe, Review, Blocked }

public sealed record CodeChange
{
    public string ProjectLogicalName { get; init; } = "";
    public string File { get; init; } = "";
    public string Description { get; init; } = "";
    public ChangeKind Kind { get; init; }
    public string? Detail { get; init; }
}

public sealed record PackageChange
{
    public string Id { get; init; } = "";
    public string? LegacyVersion { get; init; }
    public IReadOnlyDictionary<string, string> VersionsByTfm { get; init; } = new Dictionary<string, string>();
    public bool IsConditional => VersionsByTfm.DistinctBy(kv => kv.Value).Count() > 1;
    public string? BlockedReason { get; init; }
    public string? PotentialAlternative { get; init; }
}

public sealed record MigrationIssue
{
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public ChangeKind Severity { get; init; }
    public string? Project { get; init; }
    public string? DependencyPath { get; init; }
}

public sealed record ProjectMigration
{
    public string LogicalName { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public IReadOnlyList<string> ReferencePaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SourceTfms { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DestinationTfms { get; init; } = Array.Empty<string>();
    public bool RequiresSdkConversion { get; init; }
    public MappingReason MatchReason { get; init; }
    public string MatchDetail { get; init; } = "";
    public bool IsSourceOnly { get; init; }
}

public sealed record MigrationPlan
{
    public string SourceRepository { get; init; } = "";
    public string ReferenceRepository { get; init; } = "";
    public string DestinationRepository { get; init; } = "";
    public string Branch { get; init; } = "feature/multitarget";
    public IReadOnlyList<ProjectMigration> Projects { get; init; } = Array.Empty<ProjectMigration>();
    public IReadOnlyList<PackageChange> Packages { get; init; } = Array.Empty<PackageChange>();
    public IReadOnlyList<CodeChange> CodeChanges { get; init; } = Array.Empty<CodeChange>();
    public IReadOnlyList<MigrationIssue> Issues { get; init; } = Array.Empty<MigrationIssue>();
    public IReadOnlyList<string> SourceOnlyProjects { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ReferenceOnlyProjects { get; init; } = Array.Empty<string>();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
