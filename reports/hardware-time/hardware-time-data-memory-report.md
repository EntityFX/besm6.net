# Resumable CPU data memory

The functional CPU and hardware owner share the handlers for XTA, ATX, STX,
XTS, ARX, ACX, ANX, ASX, XTR, STI, ITS and WTC. The hardware owner receives
immutable read replies and resumes after writes outside calendar callbacks.
STX/XTS retain their original store/read and stack ordering. Privileged STI/ITS
retain special-register targets, including the target frozen before ITS writes.

BRZ admission does not bypass MRAM with a synchronous flush. The last free
supervisor register triggers timed oldest-first publication; an eight-slot hosted
backend waits for capacity. Panel store sequences use the same publication port.
Accepted writes survive CPU reset. Instruction-buffer clearing cancels reads
while preserving stores; assignment changes also cancel stale store admission. Cancelled and completed identities do not
replay callbacks. Arbitration remains the explicit logical FIFO policy, with
configured latency/visibility; this does not claim the physical pipeline priority.

47 new cases, both instruction halves, full CPU/supervisor/buffer state and
memory comparison with Step, input control faults, hardware and diagnostic
watchpoints, address modification, zero, reset/restart, reentrant time guards, BRZ pressure and panel
publication. Release: 3083 tests, CERN 397/397, golden 3/3, three existing skips.

The initial shared phase-loop design passed the semantic corpus but exceeded
the performance limit. Its entire series and source hashes are retained. The
accepted design calls shared transitions directly in functional execution,
without the remaining opcode redispatch. The intermediate direct-transition
experiment also remains in the results. JIT disassembly of the fast block
confirmed XTA/ATX helper inlining; these dumps remain local.

One warmup and five measured runs, fixed DATE*, preserved baseline, separate
RunLoaded/full CLI. Worst primary regression: RunLoaded 2.153%, CLI 0.725%
(limit 5%). Maximum original timing error: 7.462 ms. Baseline jobs
and every measured output are retained. Local output SHA256 is in the manifest.

Accepted 8 estimated development hours: 98 -> 90 hours remaining, stage 5
110/200 hours (55%). Selected logical CPU/MRAM transfers are connected.
CPU diagnostic transitions prohibit reentrant hardware-time advancement.

Complete automatic retirement/overlap, variable AU deadlines, independently
addressed read buffers, raw 50-bit AU input control, device DMA and the hardware
OS gate still require work. This compatibility corpus does not prove that OS gate.

[Results and manifest](hardware-time-data-memory-results.json).
