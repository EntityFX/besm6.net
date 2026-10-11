# Hardware device calendar and shared DMA

SupervisorIoController retains one port/register protocol and media layout.
Device scheduling selects functional instruction ticks or explicitly configured
nanoseconds. Tick period entry points reject hardware selection; hardware clock
entry points use the selected periods. Switching requires an idle calendar and
shared storage/port. Attachments, registers and pending interrupts survive.

Disk page/track and service-only reads, track headers and drum page/sector
exchanges use the same media transfer selection in both modes. Hardware DMA
issues raw 50-bit requests to the CPU's MRAM port, with the configured read
sampling/write visibility and FIFO banks. Ready is raised only after the final
transfer. CPU buffers/RP/RZ are untouched; CPU reset preserves device requests.
A zero issue interval explicitly means burst FIFO; positive intervals space
words and permit CPU requests between device words. These are logical bus and
peripheral configurations, not reconstructed historical electrical priorities.

Timer, serial, paper reader, console and printer phases share the nanosecond
calendar. Periodic successors are registered before host notification, so an
observer failure retains the next pulse in either calendar. Interrupted DMA
registration suppresses incomplete acknowledgment, drops unaccepted requests
and retains already accepted writes and bank occupation. Completed exchanges
remain counted if their observer fails; callbacks never become CPU avosts.

34 new cases cover both channels/directions, parity/raw controls, page/track/
service/header/drum selection, zero/last page, CPU competition, burst/spaced
issuance, reset, buffer preservation, periods/handoff, host failure and a real
shared CPU external interrupt boundary. Release: 3177 passed, CERN 397/397,
golden 3/3; three existing skips.

One warmup/five measured runs, fixed DATE*, baseline commit 3bb12c4, separate
RunLoaded/full CLI and baseline jobs. Worst primary time change:
RunLoaded 1.371%, CLI 1.519% (limit +5%). Maximum original
error 5.933 ms. These benchmarks verify the default functional path;
the hardware OS gate and expanded-memory timing are not established here.
Raw logs remain local; source, DLL, TRX and output hashes are retained.

Accepted 8 estimated development hours: 88 -> 80 remaining; 120/200 (60%).
CPU API/CLI/pacing, variable AU/input control, independent BRCh, CPU special
memory/device routes, overlap and hardware OS/expanded-memory acceptance remain.

[Results and manifest](hardware-time-device-dma-results.json).
