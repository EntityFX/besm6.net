# Reliable Execution Foundation Design

## Status and scope

This is the first of three independent simulator-completion projects:

1. reliable execution entry points (this design);
2. the complete active CERNLIB regression matrix;
3. hardware-accurate asynchronous devices and interrupts.

The current project is complete when supported jobs start reliably from a normal
checkout, raw jobs take the correct execution path, and CLI validation commands
cannot report success for a loader error or instruction-limit termination.

The project does not add new BESM-6 opcodes, new extracodes, the full CERNLIB
matrix, golden output for every example, or cycle-accurate devices.

## Evidence and current failures

- The committed `besm6.json` specifies `"tapes": "tapes"`, but a developer
  checkout stores reference images under the untracked `ref/dubna/tapes` tree.
  `besm6 run examples/algol.dub` therefore reaches `Jump to zero` from the
  repository root.
- With an absent configuration file, automatic tape discovery finds
  `ref/dubna/tapes`; the same ALGOL and BEMSH jobs compile, print their expected
  program text, and halt normally. The processor is not the cause of this
  failure.
- `MountScriptTapes` ignores a failed `MountTape`, so execution can continue with
  zero memory instead of reporting the missing volume.
- A raw-word job containing `*execute` is routed to MONSYS solely because
  `job.Execute != null`. The committed raw hello/math/io jobs consequently start
  at PC zero instead of their `*trans-main` address.
- `besm6 help` captures the temporary null help entry in its command list and
  throws after printing the other commands.
- `besm6 check` treats `NO-CONTENT` and instruction-limit termination as success.

## Chosen approach

Introduce deterministic resource resolution at the configuration/factory
boundary and validate required volumes at the loader boundary. Relative
configured paths are resolved relative to the configuration file first, then
through the existing checkout discovery rules. A missing explicit resource must
produce a diagnostic containing the requested tape and searched directory; it
must never fall through to CPU execution.

This is preferred to changing `besm6.json` to a repository-specific relative
path, which would still fail for installed binaries, and to copying tape images
into the repository, which requires a separate distribution and licensing
decision. Packaging remains a later release task; this project makes both a
developer checkout and an explicitly configured installation deterministic.

Raw program images take precedence over `*execute`: when `RawWords` is nonempty,
the loader writes them at `TransMain ?? DefaultLoadBase`, sets PC to that address,
installs the extracode hook, and runs them directly. `*execute` continues to select
MONSYS only for source-language or assembler jobs that need OS compilation.

The CLI registry is constructed without sentinel null values. `check` runs every
`.dub` through the same public `RunScript` path as `run`; loader failure and
instruction-limit termination both contribute to a nonzero exit code. `check`
only certifies execution/termination, not semantic output correctness. Exact
stdout validation belongs to integration and CERNLIB golden tests.

## Components and interfaces

### Configuration and resource resolution

`Config.Load(path)` records the absolute directory of the configuration file when
one was actually loaded. An explicitly supplied, missing configuration path is an
error; an omitted configuration may still fall back to defaults.

`Config.ResolvePath(relative)` resolves in this order:

1. an absolute existing path;
2. a path relative to the loaded configuration directory;
3. a path relative to the current directory;
4. a path relative to the application directory;
5. for the conventional `tapes` resource, the existing upward search including
   `ref/dubna/tapes`.

`MachineFactory.CreateLoader` passes `null` when the configured conventional
`tapes` location cannot be resolved, allowing `TapeImage.DefaultTapesDir()` to
perform discovery. A nonconventional explicit path remains explicit so a typo is
reported rather than silently replaced.

### Loader validation

Tape setup is split into mounting the job's explicit `*tape` cards and ensuring
the implicit MONSYS volume. Both operations check their mount results. Raw jobs
mount only explicitly requested tapes; source/assembler jobs that boot the OS
also require MONSYS. Unknown tape names and missing images throw
`ProcessorException` before boot. Diagnostics include the channel/tape id and
resolved tape directory.

### Raw execution

`RunJob` and the TUI-oriented `LoadScript` select their raw path before requesting
the implicit MONSYS volume. `RunJob` selects `RunRawWords` before evaluating the
`Execute` card. Existing address wrapping and `RunBounded` behavior remain
unchanged.

### CLI behavior

The help command receives a list containing only real commands. It must return
zero when stdout is redirected or stdin is closed.

`CheckCommand` uses `RunScript` for every discovered `.dub`. Its summary separates
normal halts, limits, runtime errors, and parse errors. Any limit, runtime error,
or parse error returns exit code 1. Empty directories are allowed and reported as
zero files; a job is never labelled successful merely because the parser did not
populate `RawWords` or `SourceLines`.

## Error handling

- Missing explicitly requested configuration: `FileNotFoundException` with its
  path.
- Missing/unknown tape image: `ProcessorException` before `BootMsDubna`.
- Bad CLI arguments retain current usage errors; this project does not add a new
  argument parser.
- `check` catches per-file exceptions, reports them, continues with other files,
  and returns failure at the end.

## Test strategy

All production changes follow red-green TDD.

1. Configuration tests reproduce the committed-config failure from a repository
   root and verify config-relative resolution plus missing explicit config errors.
2. Loader tests verify missing MONSYS and unknown requested tape fail before the
   first instruction.
3. A raw integration test uses the committed `hello.dub`, proves the current
   `Jump to zero` failure, then verifies PC/STOP/output after the fix; its obsolete
   `Ignore` attribute is removed.
4. CLI tests reproduce the help null failure and `check` returning zero on an
   instruction limit.
5. The full MSTest suite and Python trace-comparator suite must remain green.
6. Direct smoke runs from the repository root cover `name.dub`, `algol.dub`, and
   `bemsh.dub` using the normal committed configuration.

## Definition of done

- `besm6 run examples/name.dub`, `algol.dub`, and `bemsh.dub` work from the
  repository root with the normal configuration.
- A missing required tape stops before instruction execution with a useful error.
- Raw hello/math/io no longer fail because `*execute` selected MONSYS.
- `besm6 help` exits zero without an exception.
- `besm6 check` returns nonzero for limit, parse, or runtime failures and executes
  control-card-only jobs instead of reporting `NO-CONTENT`.
- All existing tests and new regressions pass; only intentional diagnostic or
  reference-disabled tests remain skipped.
