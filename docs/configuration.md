# Configuration — `migration.yml`

All sections are optional; omitted values fall back to built-in defaults.
The `plan` command can generate a skeleton file for review.

```yaml
projectMatching:
  frameworkSuffixes:
    - Net8
    - Net7
    - Net6
    - Standard
    - NetStandard
    - NetCore
    - NetCore31
    - NetFramework
    - Framework
    - Legacy

projects:
  - source: src/Core/Core.csproj
    references:
      - project: src/Core/Core.Standard.csproj
        frameworks:
          - netstandard2.0
      - project: src/Core/Core.Net8.csproj
        frameworks:
          - net8.0

packages:
  preserveLegacyVersions: true

migration:
  failOnBlocked: true
  failOnAmbiguous: true

validation:
  restore: true
  build: true
  test: true
```

## Sections

- **`projectMatching.frameworkSuffixes`** — variant suffixes stripped (after `.`/`-`
  separators or as trailing suffix) to derive the logical project name.
  Normalization never removes arbitrary name parts.
- **`projects`** — explicit A↔B mappings. Matched by path suffix against discovered
  projects. **Takes priority over every heuristic.** Use it to resolve ambiguities
  or to pin which B variants feed a logical project (and therefore its TFMs).
- **`packages.preserveLegacyVersions`** — legacy A versions remain the source of
  truth for legacy TFMs; B versions apply to new TFMs only (conditional
  `PackageVersion` entries). Legacy packages are never auto-upgraded.
- **`migration.failOnBlocked`** — abort (and roll C back) when BLOCKED issues exist.
- **`migration.failOnAmbiguous`** — reserved for strict CI gating of ambiguous mappings.
- **`validation.*`** — toggles for the per-TFM `restore` / `build` / `test` steps.

## Matching priority (for reference)

1. explicit mapping, 2. normalized logical name, 3. `ProjectGuid`,
4. `AssemblyName`, 5. `PackageId`, 6. `RootNamespace`, 7. path,
8. dependency graph, 9. other MSBuild metadata. Every automatic match records
its reason (e.g. `LogicalName = Core`).
