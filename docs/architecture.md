# Architecture

Root namespace: `Aiglusoft.CsprojMigrator`. Folder layout mirrors namespaces 1:1.

```text
CsprojMigrator
│
├── Cli               CliOptions, InteractivePrompter, Reporter
├── Repository        GitRepository (git CLI, destination only)
├── Discovery         ProjectDiscovery, LogicalProjectNormalizer, ProjectMatcher
├── Frameworks        TargetFrameworkParser, TargetFrameworkMerger
├── Dependencies      ProjectGraphBuilder, PackageAnalyzer, CompatibilityAnalyzer
├── Projects          CsprojReader, SdkConverter, CsprojWriter
├── Packages          CentralPackageManager, PackageConditionGenerator
├── Code              RoslynAnalyzer, CodeMatcher, CodeMergeEngine,
│                     ConditionalCompilationGenerator
├── Migration         MigrationPlan (models), MigrationPlanner, MigrationExecutor
└── Validation        DotNetRunner, ValidationRunner (RESTORE/BUILD/TEST per TFM)
```

Shared models live in `Models` (`ProjectInfo`, `ProjectMapping` with
`MappingReason`, `ProjectMigration`, `PackageChange`, `CodeChange` with
`Safe/Review/Blocked`, `MigrationIssue`, `MigrationPlan`); configuration
binding in `Configuration` (`MigratorConfig`, `ConfigLoader`).

## Execution pipeline (`migrate`)

1. validate A, 2. validate B, 3. validate C, 4. Git analysis,
5. project discovery (`**/*.csproj`, `bin`/`obj`/`.git` excluded),
6. csproj parsing, 7. A↔B matching, 8. dependency graphs,
9. package analysis, 10. TFM computation, 11. plan generation,
12. interactive decisions, 13. branch creation in C, 14. `.csproj` conversion,
15. package updates (`Directory.Packages.props`), 16. code changes,
17. restore, 18. build, 19. test, 20. report (`migration-report.json`).

## Design principles

- Business decisions are separated from I/O; analysis runs without modifying
  any repository (`analyze`/`--dry-run` perform zero writes).
- The plan is deterministic (deduped, case-normalized, ordered TFMs).
- Execution applies only a validated plan; C rolls back to its initial HEAD
  (`git reset --hard`) on critical failure.
- Mappings are traceable: every automatic decision carries an explainable reason.
- Code transformations use Roslyn; file-level diffs are classified
  SAFE / REVIEW / BLOCKED, and whole-file differences prefer MSBuild
  `Condition` over `#if` injection. No blind text replacement, ever.
