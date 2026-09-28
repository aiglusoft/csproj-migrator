# csproj-migrator — Documentation

© 2026 Aiglusoft — Tous droits réservés. Auteur : Imed Ben Youssef.
Ce dépôt fait partie du patrimoine logiciel d'Aiglusoft (licence propriétaire, voir `LICENSE`).

`csproj-migrator` is a .NET 8 CLI that automates the migration of legacy .NET
projects to multi-target architectures, using a modern reference repository.

## Core principle

Three local repositories, strictly separated roles:

| Repository | Role | Writable |
|---|---|---|
| **A — Source / Legacy** | Source of truth for existing code and configuration | ❌ Never |
| **B — Reference / Modern** | Modern implementations, TFMs, and dependency versions | ❌ Never |
| **C — Destination** | Migration result | ✅ Only C |

> A defines what must be preserved.
> B defines the modern variants to integrate.
> C receives the migration result.

Central rule:

```text
TFM(C) = Unique(TFM(A) ∪ TFM(all matching B projects))
```

Only B projects matched to the same logical project contribute TFMs.

## Documentation map

- [Getting started](getting-started.md) — prerequisites, build, first migration
- [CLI reference](cli-reference.md) — `analyze`, `plan`, `migrate`, `validate`, options, exit codes
- [Configuration](configuration.md) — `migration.yml` reference and explicit mappings
- [Architecture](architecture.md) — components, pipeline, object model
- [Safety rules](safety-rules.md) — the 12 mandatory rules and ambiguity policy
- [CI/CD](ci-cd.md) — non-interactive mode and automation
