# Execution owners of one machine

`MachineCore` defaults to functional execution. The shared CPU, registers,
arithmetic semantics, physical memory, buffers and device attachments remain
one machine. `UseHardwareExecution(HardwareMachineConfiguration)` selects the
staged nanosecond driver for the supervisor profile; `UseFunctionalExecution()`
selects the existing instruction-tick simulation. `Step`, `Run` and
`RunInstructions` follow the selected owner. Direct `Cpu.Step` and hosted loops
cannot bypass a selected hardware owner.

Switching requires an idle CPU and calendars: no prepared instruction, callback,
active driver or queued event. Cancel/drain events through their original owner;
no clock units are converted and no instructions/events are skipped. Configuration
is immutable once selected: reuse the same configuration object after switching.
CPU reset preserves the selected owner, memory, both clocks and device operations.
Reset of a live hardware lease still follows its cancellation boundary before
another owner can be selected. No cold device reset is implied.

Hardware durations are explicit logical values. The MRAM bank cycle retains
the published minimum; read sampling, write visibility, device delays and variable
AU completion durations are caller-selected. Missing variable AU/control entries
fail explicitly. Hardware execution currently selects the supervisor machine;
hosted Dubna extracodes/CTX are not supplied as a hardware implementation.

## CLI and JSON

A standalone raw machine image uses the separate `machine` entry point:

```powershell
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll machine examples/machine-timing.json --execution functional --speed max --stats
dotnet src/Besm6.Cli/bin/Release/net8.0/besm6.dll machine examples/machine-timing.json --execution hardware --speed original --stats
```

`--execution functional|hardware`, `--speed original|max` and `--limit N`
override valid JSON settings. Unknown options/JSON fields, invalid modes,
nonpositive limits, duplicate/out-of-range image addresses and words exceeding
50 bits are rejected. Hardware selection requires a complete timing object.
The default memory is `classical32k`; `simh512k` hardware timing additionally
requires explicit `logicalLowThreeBits` banking. This is a logical approximation,
not a reconstruction of expanded historical bank wiring.

The example contains PA to M1 and STOP, with raw command control bits. JSON
addresses, raw words and nanosecond durations are decimal. Opcode timing keys
are canonical `Opcode` names or decimal opcode numbers (including supervisor
extracodes). The input retains raw control bits, including deliberately bad
control; the CPU interprets them through its shared handlers. Omitted memory
words retain the existing machine initialization, not an inferred power-on state.

The JSON result includes STOP/limit/error status, completed instructions,
registers, functional ticks and hardware nanoseconds. Hardware statistics use
`hardwareModelSeconds`; `modelCycles` is null because the instruction cost table
is not the staged model's clock. In functional execution the existing timing
table and statistics remain in use. `original` follows absolute host deadlines
from the beginning of each run; `max` without statistics does not read a host timer.

`tools/benchmark_machine_execution.py` checks both owners/speeds and hardware
pacing with one warmup and five measurements. Its one-second control permissions
are explicitly synthetic, not historical instruction durations. Full selected-OS
hardware acceptance and the remaining AU/input-buffer rules are separate gates.

## Startup attachments and selected-OS harness

`memoryWords` accepts `panel: true` for panel registers 1..7. Panel and physical
storage addresses are separate, and raw control bits are preserved in both.
The optional top-level `devices` object selects disk images, scratch disks/drums,
serial terminals, printers and UTF-8 paper tapes. Each file attachment requires
its SHA256 and resolves relative to the JSON file. Images are copied into device
storage; running the guest does not write back to host media files.

`startTimer`/`startSerialClock` start the selected calendar after attachments.
Functional execution requires positive `timerPeriodTicks`/`serialPeriodTicks`;
hardware execution rejects tick periods and uses `timing.devices` nanoseconds.
The CLI result also contains console/serial bytes and printer rows.

The boot harness supports `--hardware-job` and public `UseHardwareExecution`.
Its operator actions and polls use `HardwareTimeline.Now.Nanoseconds`; they do
not convert completed instruction ticks into physical time. The provided
`examples/dispak-logical-timing.json` is an experimental timing template: MRAM
read bounds follow the accepted classical contract, expanded bank mapping,
write visibility, UU and variable AU/device delays are explicit logical choices.
Its 10,000 ns variable AU permissions are not historical operation measurements.
AND/OR and packing/unpacking retain their source-backed fixed cycle counts.

With verified pinned media kept in an ignored working directory, export a job:

```text
python tools/stage4_boot_probe.py --output tests-run/os-media --memory-configuration Simh512K --hardware-job examples/dispak-logical-timing.json --write-machine-job tests-run/os-media/machine.json
```

The standalone CLI can load the exported machine and attached devices. The OS
acceptance harness additionally performs the documented operator requests,
checks actual Fortran prime output and asks the OS its next question.
Use `--compare-probe` for the other speed: memory/media/state, I/O history,
nanosecond time and configuration hashes must match, not just printed text.

Hardware operator submission waits for the dispatcher idle boundary and leaves
a quiet interval after the previous panel request. The pinned guest code at
octal 07067 subtracts the previous request time and shifts the difference by
six, rejecting a zero result. The fixture therefore waits 65 slow-clock counts
(each four configured timer pulses) after both the previous request and the ready prompt before FS8. This is an operator scheduling
policy; it changes neither guest memory nor CPU/device timing. Before submission,
the harness also observes the published raw clock words used by that dispatcher
(0x2E5 current, 0x27A previous) and requires a forward difference of at least64.
Timer interrupts may coalesce while masked; nominal elapsed periods alone do not
prove the guest counter advanced. Raw physical reads avoid changing BRZ recency
or triggering CPU operand watch hooks. Request records
include the admission address, mode, interrupt state and serial readiness.

I/O observers may implement `IHardwareSupervisorIo` and identify their real
controller. Automatic device commands verify that controller's MRAM port and
calendar; a foreign or unverified bus is rejected before device execution.

BRCh (БРЧ) raw replies occupy four addressed registers paired with the current
BAK receive slot, including advancement by immediate AU commands. A register
is reserved before memory admission, ready only after its reply, consumed once
at PVR and released on cancellation/reset retirement. Retained bits are not a
read cache. Physical input checking requires an explicit verified source code;
the slot number is not assumed to encode an interruption cause. Default shared
guest memory-control/error delivery remains unchanged. Unknown physical fault
continuation and overlap rules remain outside the automatic logical profile.
