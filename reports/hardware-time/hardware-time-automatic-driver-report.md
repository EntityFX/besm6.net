# Automatic sequential hardware CPU driver

The model now owns consecutive shared CPU leases: timed fetch, data transfers,
AU binding/PVR/SPOP/IZOP and retirement. A time limit preserves a pending lease;
STOP, instruction limit, cancellation and guest faults return explicit boundaries.
CPU callbacks cannot recursively run the driver. Diagnostic fetch suppression
never starts a data transfer or an AU command.

UU minimum duration from decoded acceptance overlaps transfers. Fixed AND/OR
and packing defaults retain the existing documented sequence. Variable AU and
UU durations require an explicit logical configuration, copied on selection.
AU completion is timed from SPOP, not command issue. Missing timing is rejected
before operand/address/stack effects. This is sequential logical scheduling;
it does not establish historical variable microcode or full UU/AU overlap.

60 new cases: both instruction halves and 46 opcode forms, exact shared CPU and
supervisor state against Step, a mixed program/store forwarding, limits and
continuation, STOP, guest arithmetic/fetch faults, reset/cancellation, immutable
configuration, same-time event order and diagnostic suppression/reentrancy.
Release: 3143 passed; CERN 397/397; golden 3/3; three existing skips.

Default functional performance, one warmup/five measured runs, fixed DATE*,
baseline commit c1922a5, separate RunLoaded/full CLI and baseline jobs:
worst primary regression -0.165% / 0.831% (limit 5%).
Maximum original timing error 7.026 ms. These benchmarks check the
functional compatibility path; they do not prove hardware OS or device timing.
Raw logs remain local; source, DLL, TRX and output hashes are retained.

Accepted 2 estimated development hours: 90 -> 88 remaining; 112/200 (56%).
The public API/JSON/CLI profile and host pacing, variable AU/input-control work,
independent BRCh, devices/DMA and hardware OS acceptance remain unfinished.
Special-memory and device opcodes are rejected before synchronous execution.

[Results and manifest](hardware-time-automatic-driver-results.json).
