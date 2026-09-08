# Architecture cleanup regression baseline

Baseline captured on 2026-09-08 from `codex/architecture-cleanup` after commit
`9edaa16` and before the remaining assembly decomposition.

## Commands

```powershell
dotnet test Besm6.sln -c Release --no-restore --logger "console;verbosity=minimal"
py -3 -m unittest discover -s tools/tests
```

Project discovery at this checkpoint:

| Project | Discovered | Result |
|---|---:|---|
| `Besm6.Tests` (legacy oracle) | 409 | 405 passed, 4 skipped |
| `Besm6.Architecture.Tests` | 10 | 10 passed |
| `Besm6.Processor.Tests` | 92 | 92 passed |
| `Besm6.Assembler.Tests` | 8 | 8 passed |

The project totals contain 519 executions and 518 unique test names because
`AdvanceTo_Backward_Throws` is intentionally present in both the legacy and extracted
suites during migration. The acceptance inventory is the 506 pre-cleanup scenarios
plus 12 new architecture/assembly-boundary scenarios. The final split must retain all
506 original scenarios and may add further focused tests.

The four accepted skips are:

- `Case_MatchesExpectFile`
- `W303_LoopsForever`
- `DumpAtDivergence`
- `TraceAlgol_Last30Instructions`

The Python trace tooling baseline is 50 tests, all passing. Canonical TSV keeps
`pc` and `pc_a` as compatibility field names; user-facing labels use register `K`.
