# CI/CD

## Non-interactive migration

```bash
csproj-migrator migrate \
  --source ../repo-a \
  --reference ../repo-b \
  --destination ../repo-c \
  --branch feature/multitarget \
  --non-interactive \
  --config migration.yml
```

Rules in CI mode:

- No prompts, no confirmations, no silent guesses.
- Ambiguous mappings fail with `MIGRATION_AMBIGUOUS_MAPPING` (exit 2) — resolve
  them with explicit `projects:` entries in `migration.yml` (generate a skeleton
  with the `plan` command).
- BLOCKED issues fail the run when `migration.failOnBlocked` is `true`, and C
  is rolled back to its initial HEAD.
- A dirty destination (uncommitted changes) refuses the run before any write.

## Recommended pipeline stages

1. `analyze` (report, fails on blockers) → publish output as a check annotation.
2. `plan --plan-out migration.yml` → review the mapping skeleton in the PR.
3. `migrate --non-interactive --config migration.yml` on a clean C checkout.
4. `validate --destination` results are included in `migration-report.json`.

## This repository's own CI

`.github/workflows/dotnet.yml` builds and tests the tool itself on `main`
(`dotnet restore` → `dotnet build --no-restore` → `dotnet test`), using .NET 8
on `ubuntu-latest`. It does not run migrations; it guards the tool's quality
(19 unit tests covering normalization, TFM merge, matching, package conditions,
per-TFM compatibility, and `#if` generation).
