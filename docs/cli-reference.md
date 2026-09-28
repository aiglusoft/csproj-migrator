# CLI reference

## Global syntax

```text
csproj-migrator <command> [options]
```

## Commands

### `analyze`

Read-only analysis. Modifies nothing.

```bash
csproj-migrator analyze --source <dir> --reference <dir> --destination <dir> [--config <file>]
```

Reports: detected projects, logical mappings with reasons, TFMs,
dependencies, packages, conflicts, incompatibilities, ambiguities.

### `plan`

Builds a migration plan for review and CI reuse.

```bash
csproj-migrator plan --source <dir> --reference <dir> --destination <dir> [--config <file>] [--plan-out <file>]
```

Writes `migration.yml` (explicit mappings) and a deterministic `.json` plan.

### `migrate`

Applies the plan to the destination repository only.

```bash
csproj-migrator migrate --source <dir> --reference <dir> --destination <dir>
  [--branch <name>] [--dry-run] [--non-interactive] [--config <file>]
```

Prompts for confirmation and resolves ambiguities interactively unless
`--non-interactive` is set. Writes `migration-report.json` into C.

### `validate`

Runs per-project × per-TFM validation in the destination.

```bash
csproj-migrator validate --destination <dir>
```

Steps per TFM: `RESTORE`, `BUILD`, `TEST` (test step only for test projects).

## Options

| Option | Commands | Default | Description |
|---|---|---|---|
| `--source <dir>` | analyze, plan, migrate | — | Legacy repository A (read-only) |
| `--reference <dir>` | analyze, plan, migrate | — | Modern repository B (read-only) |
| `--destination <dir>` | all | — | Destination repository C (only writable repo) |
| `--branch <name>` | migrate | `feature/multitarget` | Dedicated branch created in C |
| `--dry-run` | migrate | off | Analyze and report; perform zero writes |
| `--non-interactive` | plan, migrate | off | CI mode: never prompt, fail on ambiguity |
| `--config <file>` | analyze, plan, migrate, validate | built-in defaults | `migration.yml` configuration file |
| `--plan-out <file>` | plan | `migration.yml` | Plan output path |

There is intentionally **no `--target-framework` option**: destination TFMs are
always computed as `Unique(TFM(A) ∪ TFM(matched B))`.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (analyze/plan report no blockers) |
| `1` | Runtime error, validation failure, dirty destination, user abort |
| `2` | Blocked/ambiguous state (`MIGRATION_AMBIGUOUS_MAPPING`, `MIGRATION_BLOCKED_TFM`) |

## Interactive behavior

By default the CLI explains every required decision (e.g. ambiguous candidates
with their TFMs) and asks before writing. In `--non-interactive` mode it prints
a machine-readable error with the resolution instead of guessing:

```text
ERROR MIGRATION_AMBIGUOUS_MAPPING

Project:
  Core

Candidates:
  Core.Net8.csproj
  Core.Standard.csproj

No deterministic mapping found.

Resolution:
  Add an explicit mapping to migration.yml.
```
