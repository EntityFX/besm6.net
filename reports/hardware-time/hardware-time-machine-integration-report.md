# Shared functional and hardware machine: consolidated integration

One machine now selects functional instruction execution or the staged hardware
owner through API, strict JSON and the standalone machine CLI. Registers,
arithmetic, memory and devices are shared. Both clock domains remain independent;
CPU reset retains memory, devices, queues, timing configuration and clock values.
Direct/nested execution and owner switches with pending work are rejected.

Four addressed raw arithmetic input registers follow the BAK receive slot,
including immediate-command advancement. Replies become ready after transfer,
are consumed once at PVR, and release their reservation on cancellation/reset.
Physical input checking requires an explicit verified source code. File-backed
startup attachments require hashes and remain local memory copies; panel and
physical words are separate. Device observers identify their actual hardware bus.

Release acceptance:3231 passed across9 projects, CERN397/397, golden3/3,
three existing skips. One warmup/five measurements, fixed DATE*, baseline
0bc608b, separate RunLoaded/full CLI, including dh0/mp0. Worst primary
regressions: RunLoaded 1.106%, CLI 0.639% (limit5%). The first public-owner
CLI mp3 series exceeded the limit (+13.481%); its cause remains unestablished
and the series is retained. The consolidated final series passes.
Maximum functional original error: 4.459 ms.
The24-run public-owner matrix passes; its explicitly synthetic hardware pacing
fixture has maximum original error 10.350 ms.

The pinned expanded512K DISPAK image boots through the hardware owner, reads the
complete input tape, executes the actual Fortran job, prints all9 reference
primes, finishes and answers the next OS request. All8 OS checks pass.
Original/max have identical guest state, memory/media and I/O/event history,
359999879 completed commands and430.696050250 model seconds; legacy ticks stay0.
The instrumented host cannot maintain this model's original tempo: original
wall time 934.465s. This capacity limit
does not change guest state or skip instructions. Host time and allocations for
this diagnostic run are reported separately from functional benchmarks.

The pinned guest panel dispatcher rejects requests separated by fewer than64
guest clock counts. The harness submits FS8 at the idle boundary after the
prompt and quiet interval, observing published raw clock words without changing
guest memory or buffer recency. Earlier failures and the truncated180M run remain
local; only the complete360M pair proves OS acceptance.

This is a logical staged model, not an electrical replica. Source-backed fixed
AU/read timings remain distinct from explicit variable-AU, UU, expanded banking,
write-visibility and device parameters. Unknown physical fault continuation and
overlap rules are not guessed. Dubna retains functional execution by default.
No host replacement of guest arithmetic or OS work is used.

Accepted40 estimated development hours:76 ->36 remaining;164/200 (82%).
The phase remains open:20h independent final acceptance and16h consolidated
documentation/delivery remain. Raw media, detailed traces and archives stay
outside Git; these small JSON/CSV files retain validation and output hashes.

[Results and manifest](hardware-time-machine-integration-results.json).
