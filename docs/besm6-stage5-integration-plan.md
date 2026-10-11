# Stage 5: consolidated 40-hour integration block

Agreed next block: 40 estimated development hours, with complete tests and
benchmarks after the related code changes. This is planning effort, not agent
wall time. Acceptance reduces the remaining estimate from76 to36 hours;
no hours are closed before evidence is collected.

| Area | Hours | Concrete result |
|---|---:|---|
| Public owner/API/JSON/CLI | 3 | Complete the implemented public execution selection and resolve its performance acceptance in the consolidated final run |
| AU and input buffers | 5 | Addressed raw operand input, readiness/control boundaries and shared arithmetic completion/fault policy; fixed source-backed timings and explicit logical variable durations remain distinguishable |
| Hardware devices and OS | 20 | The selected Simh512K configuration boots through the public hardware owner; clocks, DMA, operator input and interrupts use the same nanosecond calendar; a real Fortran job finishes and OS answers its next request |
| Consolidated integration | 12 | Profile/speed matrix, repeated runs/reset, complete Processor/Runtime/CLI and CERN397/golden, Release benchmarks/tempo; output/state/event hashes and reports |

Read historical books and the pinned SIMH configuration before selecting each
rule. Unknown microcode/overlap/fault-return rules are not filled in by guesses.
Logical choices are explicit configuration and documented as such. CPU semantics
remain shared; no hosted replacement of guest Fortran or OS computations is allowed.

Implementation order inside the one block:

1. Audit existing AU control/read-buffer nodes and raw memory replies; connect
   readiness/input control without copying instruction arithmetic or shadowing memory.
2. Complete device/calendar interfaces needed by the selected OS and adapt the
   operator/boot harness to hardware time, with explicit clock periods and immutable
   media hashes. Retain the functional path as a separate reference profile.
3. Execute the guest job and subsequent OS interaction through shared CPU handlers;
   close missing routes from actual failures and source evidence.
4. Run the full acceptance matrix at the end, then save small reports/JSON/CSV and
   commit/push the complete block. Raw media, traces and archives stay local.

The remaining36 hours after successful acceptance comprise20h of final independent
acceptance and16h of consolidated documentation/delivery. The phase is not complete
until those remaining gates and the full objective audit pass.


The API/CLI component passed3218 tests, CERN397/397, golden3/3 and its public
hardware pacing/parity fixture, but its first CLI mp3 measurement exceeded the
5% performance limit (+13.481%). All samples remain local. At the start of this block it had not been
accepted or committed; it was included in this40-hour block, without another
small-component rebuild/test cycle. The final gate must resolve this concern.


The consolidated block is accepted:3231 tests, CERN397/397, golden3/3,
hardware OS8/8 with original/max state parity, all9 Fortran reference values,
RunLoaded/CLI within5% and the24-run owner/speed matrix. Remaining estimate
76 ->36h;164/200h (82%). The phase stays open for the remaining36h.
[Acceptance report](../reports/hardware-time/hardware-time-machine-integration-report.md).
