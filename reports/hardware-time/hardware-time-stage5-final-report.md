# Stage 5 final logical machine acceptance

The functional simulation and staged hardware model are separate execution
owners of one CPU, register set, arithmetic implementation, memory and device
set. Each owns its execution calendar; nanoseconds and instruction ticks are
never converted. API/JSON/CLI choose the owner and independently choose speed.
One device configuration now supplies both explicit clock rates. CPU reset
preserves memory, calendars, queues and devices; cold device reset is not implied.

Independent validation reuses100 existing book/Fraction vectors, without
deriving expected bits from the production ALU. All400 owner/speed combinations
pass. Checks include registers, visible memory, STOP, notifications and clocks.
Supervisor faults and extracodes have explicit vector/return contracts; known
Dubna/book R/Y differences remain documented compatibility boundaries.

Release acceptance:3634 passed across9 projects, CERN397/397, golden3/3,
three unchanged existing skips. Baseline200f13b; one warmup/five measurements,
fixed DATE*, baseline dh0/mp0, separate RunLoaded and full CLI. The first RunLoaded
series exceeded5% at mp1 (+13.942%); the complete repeat passes with worst
regression 1.495%. Its mp1 regression is0.123%. CLI worst regression is
-0.173%. Both raw series are retained; the cause of variation is not established.
Maximum functional original error is 7.952ms. The24-run owner
matrix passes. Synthetic hardware pacing uses explicit test delays, with maximum
error 11.950ms.

The fresh pinned Simh512K DISPAK pair runs the actual Fortran job, prints9
reference primes, finishes and answers the next OS request. All8 checks pass.
Original/max match full guest state, memory/media and I/O/event history:
359999879 completed commands,430.696050250 model seconds, functional ticks0.
Original diagnostic host time is 967.688s;
reported capacity is False. Instrumented
host throughput is separate from model timing and functional benchmarks.
The short exported-job CLI check ran in separate machines during original's
diagnostic OS run. No timing benchmark overlapped the OS pair; diagnostic wall
times are not used to claim a hardware throughput improvement.

This is a logical staged machine with explicit limits, not an electrical replica.
Published fixed AU cycles and MRAM read bounds remain distinct from configured
variable AU/UU completion, expanded banking, write visibility and device delays.
Unknown physical fault continuation/overlap is rejected rather than guessed.
Dubna remains functional by default; guest arithmetic and OS work are not replaced
with host shortcuts. The acceptance scope is the selected configuration, not all
historical operating systems or undocumented electrical schedules.

Final36 estimated development hours:36 ->0 remaining,200/200. Delivery still
requires the narrow commit/push and final objective audit; generated reports
alone do not prove delivery. Media, archives and full traces from this final
series remain outside Git.

[Results and manifest](hardware-time-stage5-final-results.json).
