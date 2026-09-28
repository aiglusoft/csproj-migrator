# Safety rules

These rules are mandatory and enforced by design, not by convention.

| # | Rule |
|---|---|
| 1 | Never modify A (source / legacy). |
| 2 | Never modify B (reference / modern). |
| 3 | C (destination) is the only writable repository. |
| 4 | Never auto-delete an A-only project (kept unchanged, reported as INFO). |
| 5 | Never auto-import every B-only project (reported as INFO, usable as reference). |
| 6 | Never auto-upgrade a legacy package version. |
| 7 | Never silently replace one package with another (alternatives are suggested, never installed). |
| 8 | Never silently resolve an ambiguous mapping (prompt, or fail in CI). |
| 9 | Never delete code without analysis. |
| 10 | Never declare a TFM compatible without per-TFM dependency analysis. |
| 11 | Every compilation error is a real error, never an ignored warning. |
| 12 | When uncertain: `ANALYZE → REPORT → REVIEW`. |

## Uncertainty policy

```text
analyser → expliquer → demander une décision → appliquer.
```

Concretely: ambiguity surfaces as an explained prompt (or
`MIGRATION_AMBIGUOUS_MAPPING` in `--non-interactive`), incompatibilities carry
the full dependency path (`Core → Data → Legacy.Database: net472 only`), and
code diffs between A and B variants are classified REVIEW/BLOCKED for human
validation instead of being merged blindly.
