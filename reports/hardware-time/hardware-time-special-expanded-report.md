# Supervisor control routes and expanded logical MRAM

REG, peripheral exchanges, privileged modifier aliases and supervisor extracode
entries now retire through shared instruction handlers at configured UU permission.
Device commands reject functional calendars and separate MRAM queues before
CPU effects. Hosted extracodes and hosted CTX remain outside this automatic route.
Control durations stay explicitly configured; supervisor extracode numbers absent
from the Opcode enum are valid control-table keys, not AU timing entries.

Default documented banking still rejects expanded storage. Explicit
LogicalLowThreeBits permits eight FIFO banks across Simh512K using low physical
address bits. Read sampling, write visibility and bank cycle are configured;
the published minimum 2000 ns cycle remains. This is a logical approximation,
not evidence of historical expanded-memory wiring.

17 additional tests cover both command halves, delay before REG publication,
register-control avost, extracode ranges, device calendar rejection, mathematical
mapped fetch/data in the last page, full-capacity raw controls, CPU/DMA bank
contention and reset. Release: 3194 passed across nine projects, CERN397/397,
golden3/3, three existing skips. Initial fixture failures are retained locally:
privileged command fetch is direct and machine-initialized zero has control bits.
The Runtime suite was rerun after correcting those fixtures and adding coverage.

Sequential Release runs: one warmup, five measurements, fixed DATE*, baseline
60f2fac. Baseline jobs retained. Worst primary time change: RunLoaded 1.878%,
CLI 1.077% (limit +5%); maximum original error 9.335 ms.
The first RunLoaded series exceeded the limit on mp1 (+5.485%); all its
samples and the first CLI series are preserved alongside the A/A control and
one predefined confirmation series. Confirmation was requested to investigate
reproducibility, with unchanged binaries and alternating run order. The figures
above describe the confirmation series; the first failure is not erased.
These benchmarks verify functional compatibility; they do not establish the full
hardware OS gate. Raw outputs remain in ignored tests-run; hashes are retained.

Accepted 4 estimated development hours: 80 -> 76 remaining; stage5 124/200 (62%).
CPU API/CLI/pacing, variable AU/input control, independent BRCh, selected OS
hardware execution and consolidated profile/speed acceptance remain.

[Results and manifest](hardware-time-special-expanded-results.json).
