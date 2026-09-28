# Csproj Migrator

CLI .NET 8 automating legacy → multi-target migration using three local repositories:

- **A — Source / Legacy**: read-only source of truth (never modified).
- **B — Reference / Modern**: read-only modern variants (never modified).
- **C — Destination**: the only repository that is modified, on a dedicated branch.

Core rule: `TFM(C) = Unique(TFM(A) ∪ TFM(all matching B projects))` — only B projects
matched to the same logical project contribute TFMs.

## Commands

```bash
csproj-migrator analyze --source ../repo-a --reference ../repo-b --destination ../repo-c
csproj-migrator plan    --source ../repo-a --reference ../repo-b --destination ../repo-c [--plan-out migration.yml]
csproj-migrator migrate --source ../repo-a --reference ../repo-b --destination ../repo-c [--branch feature/multitarget] [--dry-run] [--non-interactive] [--config migration.yml]
csproj-migrator validate --destination ../repo-c
```

- `analyze`: read-only report (projects, mappings, TFMs, conflicts, ambiguities).
- `plan`: writes `migration.yml` (explicit-mapping skeleton for review) + `.json` deterministic plan.
- `migrate`: applies the plan to C only (SDK conversion, `TargetFrameworks`, central
  `Directory.Packages.props` with per-TFM conditional versions, branch creation, per-TFM
  restore/build/test, `migration-report.json`). Supports `--dry-run` (zero writes).
- `validate`: per-project × per-TFM `restore` / `build` / `test`.

Ambiguous mappings are asked interactively by default; `--non-interactive` fails with
`MIGRATION_AMBIGUOUS_MAPPING` (exit 2) instead of guessing. Legacy package versions are
preserved; modern B versions apply to new TFMs only. No blind text replacement — C#
analysis uses Roslyn and non-trivial diffs are classified REVIEW/BLOCKED.

## Build & test

```bash
dotnet build
dotnet test
```

See `Spécification technique — Csproj Migrator.md` for the full specification (§1–§57).
