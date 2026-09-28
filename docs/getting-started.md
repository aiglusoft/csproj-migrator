# Getting started

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (8.0.x)
- Git (branch management and rollback in the destination repository)
- Three local checkouts: legacy source (A), modern reference (B), destination (C)

## Build from source

```bash
dotnet restore
dotnet build
dotnet test
```

The CLI binary is produced at `src/CsprojMigrator/bin/Debug/net8.0/csproj-migrator.dll`
and can be run with `dotnet run --project src/CsprojMigrator -- <command> …`.

## Your first migration (safe path)

1. **Analyze** — read-only, never modifies anything:

   ```bash
   csproj-migrator analyze \
     --source ../repo-a \
     --reference ../repo-b \
     --destination ../repo-c
   ```

   Review detected projects, logical mappings with their reasons, computed
   destination TFMs, conflicts, and ambiguities.

2. **Plan** — emit a reviewable plan:

   ```bash
   csproj-migrator plan \
     --source ../repo-a \
     --reference ../repo-b \
     --destination ../repo-c \
     --plan-out migration.yml
   ```

   This writes `migration.yml` (explicit-mapping skeleton for review and CI reuse)
   plus a deterministic `.json` plan.

3. **Dry-run** — prove zero side effects before touching C:

   ```bash
   csproj-migrator migrate \
     --source ../repo-a \
     --reference ../repo-b \
     --destination ../repo-c \
     --dry-run
   ```

4. **Migrate** — apply to C on a dedicated branch (default `feature/multitarget`):

   ```bash
   csproj-migrator migrate \
     --source ../repo-a \
     --reference ../repo-b \
     --destination ../repo-c
   ```

5. **Validate** — per-project × per-TFM restore / build / test:

   ```bash
   csproj-migrator validate --destination ../repo-c
   ```

## Example scenario

```text
A: Core.csproj (net472, legacy non-SDK)
B: Core.Net8.csproj (net8.0) + Core.Standard.csproj (netstandard2.0)
C result: SDK-style Core with net472;netstandard2.0;net8.0,
          Newtonsoft.Json 12.0.3 kept for net472,
          13.0.3 applied to the new TFMs via Directory.Packages.props.
```
