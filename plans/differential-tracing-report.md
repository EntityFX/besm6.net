# Differential tracing and processor completion report

## Repository state

- C# baseline HEAD: `a7c96952ef799d0672974e9716c7195f5155c560`
- Baseline branch: `main`; baseline working tree was clean.
- Implementation worktree: `codex/differential-tracing` at
  `C:\projects\besm6.net\.worktrees\differential-tracing`.
- C++ source HEAD: `ee2a098a69cd808c25e2e42205ab9f61a3372850`.
- C++ tracing binary: `Version 0.1.418-rebuild`.

## Trace infrastructure

The C# canonical trace now writes one physical TSV row per executed instruction.
Each row contains instruction identity, PRE and POST architectural state, including
PC/half, ACC/RMR, RAU, modifier state, intercept state, and M0..M15. `half` denotes
the executed half, captured before PC/half advance. STOP and throwing terminal
extracodes also write complete POST state.

`tools/diff_trace.py` streams both the canonical C# format and the legacy five-line
C++ format. It reports the first FETCH/control/PRE/POST divergence with five rows
of context. A physically incomplete final legacy record is reported as
`TRACE_TRUNCATED`, not rejected as malformed and not treated as `MATCH`.

Regression coverage includes:

- PRE-execution PC/half and exact left/right RK selection;
- one header and one physical TSV row per instruction;
- complete STOP and throwing-E74 rows;
- first POST/FETCH divergence and context reporting;
- legacy C++ parsing and truncated-terminal-record classification.
- sequence-number alignment and CONTROL/FLOW precedence over a simultaneous
  machine-word mismatch.

Throwing extracodes are finalized at the correct boundary: terminal E74 records
its POST state before propagating, while an exception handled by MONSYS records
POST only after stack correction and intercept have changed architectural state.

## Bootstrap and initial state

The existing bootstrap regressions pass for raw memory `02010..02023` and support
table `03000..03010`.

Initial state before the first bootstrap instruction is confirmed as:

```text
PC=1032 decimal = 02010 octal
half=LEFT
ACC=0  RMR=0  RAU=0  MOD=0  apply_mod=false
M0..M15=0
```

## First divergence and fix: E50 capability mask

The first original divergence was:

```text
Sequence:    2384231
PC:          1445 decimal = 02645 octal
Half:        R
Raw48/RK:    003000068080 / 068080
Instruction: *50 070200
PRE:         all compared architectural fields equal
POST:        only ACC differed
C++ ACC:     000000008000
C# ACC:      000000001000
```

C++ `e50.cpp` returns octal `0'0010'0000`, which is `0x8000`. C# returned decimal
4096 (`0x1000`) because of an octal-literal conversion error. The handler now
returns `0x8000UL`.

`E50_070200_ReturnsCppCapabilityMask` failed with actual `0x1000` before the fix
and passes with `0x8000` after it.

## Second divergence and fix: tape release/assignment

After the E50 fix, the first divergence moved forward to:

```text
Sequence:    2412595
PC:          1309 decimal = 02435 octal
Half:        R
Raw48/RK:    1A8001108BFE / 108BFE
Instruction: XTA 04000(1)
PRE:         all compared architectural fields equal
POST ACC:    C++=0000000C5300, C#=EA0000090000
```

XTA itself was correct. Address `04000` contained different data because C# had
not released disk unit `030`, then incorrectly reported that `librar.12` had been
mounted while MONSYS still occupied that unit.

The C++ release mask maps accumulator bit `47 - disk_index` to units `030..067`.
C# incorrectly used low bits `0..15`. The fix:

- implements the full 32-unit high-order-bit mapping;
- removes both unit and tape-id indexes when releasing a tape;
- rejects mounting a different tape on an occupied unit;
- continues to accept an idempotent mount of the same tape;
- enforces the C++ disk-unit range `030..067`;
- preserves tape-id lookup when the same tape id is mounted on more than one
  unit and one instance is released.

`MountTape_DifferentTapeOnOccupiedUnitReturnsFalse` and
`ReleaseTapes_UsesHighOrderAccumulatorBitForUnit030` both failed before their
respective fixes and pass afterward.

## Scope of conditional diagnostics

The plan's memory-write trace is a conditional diagnostic for a FETCH mismatch.
No FETCH mismatch remains: the two instruction streams have equal words and
architectural state through normal completion. Adding coordinated hooks to every
C++ and C# memory-write path without a failing case would therefore be a large,
unvalidated patch and was deliberately not done.

The legacy C++ trace and `dubna_ref.exe` used for this comparison are retained as
diagnostic artifacts. The local `ref/dubna` checkout does not contain the source
patch that produced that tracing binary, so exact regeneration of this legacy
artifact is not self-contained. This limitation does not affect the saved trace
comparison or the C# regression suite, but should be addressed before treating
the C++ tracing binary as a maintained project tool.

## Full architectural comparison

After both semantic fixes, the streaming comparison result is:

```text
Classification: TRACE_TRUNCATED
Sequence:       2805937
PC:             279 decimal = 00427 octal
Half:           L
Raw48/RK:       03C000090000 / 03C000
Instruction:    E74
PRE STATE MATCH:YES
C++ trace:      identity and PRE only; no physical POST record
C# trace:       complete identity/PRE/POST row
```

All 2,805,937 preceding complete instruction records match. The PRE state of the
terminal E74 matches as well. The C++ tracer is physically truncated because E74
throws before it can append POST; there is no remaining architectural divergence
to analyze.

## Output parity fix: E64 lifecycle

With processor state matching, `a400` and `z005` still differed only in text
layout: C# inserted an initial LF and blank lines between E64 calls.

The cause was a lifecycle mismatch. C# reset the E64 buffer and called
`E64Finish()` after every E64. C++ preserves the buffer across calls, starts with
`e64_skip_lines=0`, and flushes only at processor stop/exception (or before E71
terminal I/O). C# now follows that lifecycle and `DubnaLoader` flushes output at
the same terminal and exception boundaries.

The focused E64 separator test failed as `"\nA A\n"` before the fix and passes as
`"A A"` afterward. Buffered output tests explicitly finish the stream and verify
the single final LF.

## Other repaired diagnostic defect

`DiagShiftTest.DumpAtDivergence` no longer writes to a hard-coded
`E:\Projects\...` path. It derives the repository root from the located sample and
writes under that worktree's `tests-run` directory. The test now remains an
intentional diagnostic skip instead of failing because another developer's drive
does not exist.

## Final verification

Fresh Release verification in the implementation worktree:

```text
dotnet build src/besm6.net/besm6.net.sln -c Release --no-restore
Build succeeded: 0 warnings, 0 errors

dotnet test src/besm6.net/besm6.net.sln -c Release --no-build --no-restore
Passed: 435, Failed: 0, Skipped: 4, Total: 439

CERNLIB a400: exact expected-output match, 2,805,937 instructions
CERNLIB z005: exact expected-output match, 1,827,079 instructions

Python trace-diff tests: 7 passed

Full trace comparison with sequence alignment:
TRACE_TRUNCATED only at terminal E74 sequence 2,805,937;
identity and PRE match, all preceding complete records match
```

The skipped tests are explicitly marked diagnostic/unsupported cases; there are
no unexpected skips or failures.
