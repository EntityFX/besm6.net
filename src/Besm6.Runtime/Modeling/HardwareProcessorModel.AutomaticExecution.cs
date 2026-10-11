using Besm6.Runtime.Timing;

namespace Besm6.Runtime.Modeling;

internal enum HardwareRunBoundary { TimeLimit, InstructionLimit, Stop, GuestFault, Cancelled, WaitingForSignal }
internal readonly record struct HardwareRunOutcome(HardwareRunBoundary Boundary, ulong Completed,
    HardwareInstant Time, HardwareInstructionOutcome? Instruction);

internal sealed partial class HardwareProcessorModel
{
    private HardwareExecutionConfiguration? _automaticConfiguration;
    private bool _automaticRunning, _automaticOwnsInstruction, _automaticStagesStarted;
    private bool _automaticRetirementReady;
    private HardwareEventToken _automaticRetirementEvent;

    internal void ConfigureAutomaticExecution(HardwareExecutionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        EnsureDriverAllowed();
        if (_automaticRunning || _pendingInstruction.HasValue || _processor.HasPreparedInstruction)
            throw new InvalidOperationException("Configure the automatic driver at an idle CPU boundary.");
        if (_automaticConfiguration is not null && !ReferenceEquals(_automaticConfiguration, configuration))
            throw new InvalidOperationException("This machine's automatic timing configuration is already selected.");
        CreateBufferedMemory(configuration.Memory, configuration.BufferHit);
        CreateArithmeticController(configuration.ArithmeticCycle);
        _automaticConfiguration = configuration;
    }

    /// <summary>
    /// Own selection and retirement of consecutive shared CPU leases. Events,
    /// PVR, SPOP and immutable memory replies retain their existing boundaries.
    /// A time limit preserves a live lease for continuation; reset/cancel and
    /// guest faults return to the caller, which owns interrupt-return policy.
    /// Neither legacy ticks nor host waiting are performed here.
    /// </summary>
    internal HardwareRunOutcome AdvanceAutomaticTo(HardwareInstant time, ulong instructionLimit)
    {
        EnsureDriverAllowed();
        if (_automaticRunning) throw new InvalidOperationException("Nested automatic execution is not allowed.");
        if (instructionLimit == 0) throw new ArgumentOutOfRangeException(nameof(instructionLimit));
        if (time.Nanoseconds < Timeline.Now.Nanoseconds) throw new ArgumentOutOfRangeException(nameof(time));
        var configuration = _automaticConfiguration ?? throw new InvalidOperationException("Select an explicit hardware execution configuration first.");
        if (_pendingInstruction.HasValue && !_automaticOwnsInstruction)
            throw new InvalidOperationException("A manually prepared command is owned by another driver.");
        ulong before = CompletedInstructions;
        HardwareRunOutcome Result(HardwareRunBoundary boundary, HardwareInstructionOutcome? outcome = null) =>
            new(boundary, CompletedInstructions - before, Timeline.Now, outcome);
        _automaticRunning = true;
        try
        {
            while (true)
            {
                if (!_pendingInstruction.HasValue)
                {
                    if (Timeline.Now.Nanoseconds >= time.Nanoseconds) return Result(HardwareRunBoundary.TimeLimit);
                    if (PendingArithmeticInterruption != 0) return Result(HardwareRunBoundary.WaitingForSignal);
                    StartTimedInstructionFetch(configuration.Memory, configuration.BufferHit);
                    _automaticOwnsInstruction = true;
                }
                if (!_automaticStagesStarted && CurrentInstruction is { Instruction: not null } instruction)
                    StartAutomaticStages(instruction, configuration);
                HardwareInstant next = _completionReady || _cancellationRequested || !HasPendingInstruction ?
                    _completionReady && _automaticStagesStarted && !_automaticRetirementReady ?
                        Timeline.NextEventTime ?? time : Timeline.Now : Timeline.NextEventTime ?? time;
                if (next.Nanoseconds > time.Nanoseconds) next = time;
                bool hasSignal = Timeline.NextEventTime.HasValue || _completionReady || _cancellationRequested || !HasPendingInstruction;
                var outcome = AdvanceTo(next);
                if (outcome is { } completed)
                {
                    if (completed.Status == HardwareInstructionStatus.Cancelled) return Result(HardwareRunBoundary.Cancelled, completed);
                    if (completed.Status == HardwareInstructionStatus.GuestFault) return Result(HardwareRunBoundary.GuestFault, completed);
                    if (completed.Stopped) return Result(HardwareRunBoundary.Stop, completed);
                    if (CompletedInstructions - before >= instructionLimit) return Result(HardwareRunBoundary.InstructionLimit, completed);
                }
                if (Timeline.Now.Nanoseconds >= time.Nanoseconds) return Result(HardwareRunBoundary.TimeLimit, outcome);
                if (!hasSignal && CurrentInstruction is null) return Result(HardwareRunBoundary.WaitingForSignal, outcome);
            }
        }
        finally { _automaticRunning = false; }
    }

    private static bool IsAutomaticRegisterOrControl(Opcode opcode) => opcode is
        Opcode.Rte or Opcode.Yta or Opcode.Asn or Opcode.Ntr or Opcode.Ati or Opcode.Ita or
        Opcode.Mtj or Opcode.JPlusM or Opcode.Utc or Opcode.Vtm or Opcode.Utm or Opcode.Uza or
        Opcode.U1a or Opcode.Uj or Opcode.Vjm or Opcode.Ij or Opcode.Stop or Opcode.Vzm or
        Opcode.V1m or Opcode.Op36 or Opcode.Vlm;

    private void StartAutomaticStages(HardwareInstructionHandle instruction, HardwareExecutionConfiguration configuration)
    {
        var lease = _pendingInstruction!.Value;
        var opcode = instruction.Instruction!.Value.Opcode;
        // All timing is validated before accepting address/stack/register effects.
        var retirement = Timeline.Now + configuration.ControlDuration(opcode);
        bool arithmetic = _processor.CanCaptureArithmeticOperand(in lease);
        HardwareDuration? arithmeticDelay = arithmetic ? configuration.ArithmeticDuration(opcode) : null;
        if (!arithmetic && !_processor.CanBeginMemoryInstruction(in lease) &&
            !_processor.IsPreparedInstructionSuppressed(in lease) && !IsAutomaticRegisterOrControl(opcode))
            throw new NotSupportedException($"{opcode} requires its hardware device/special-memory route before automatic execution.");
        _automaticRetirementEvent = Timeline.ScheduleAt(retirement, () =>
        {
            _automaticRetirementEvent = default;
            _automaticRetirementReady = true;
        });
        if (arithmetic)
        {
            bool immediate = ArithmeticCommandEncoding.IsImmediate(opcode);
            var command = BindArithmeticInstruction(instruction, configuration.ArithmeticCycle, immediate,
                immediate ? null : new ArithmeticOperandBuffer(ArithmeticOperandBufferKind.ReadNumbers, 0),
                automaticCompletion: true, logicalCompletionDelay: arithmeticDelay);
            var controller = CreateArithmeticController(configuration.ArithmeticCycle);
            controller.GrantCommandPermission();
            if (immediate)
            {
                if (!controller.TryAcceptDirectOperand(command))
                    throw new InvalidOperationException("The automatic UU could not accept its direct operand.");
            }
            else RequestBoundArithmeticOperand(instruction, command);
        }
        else if (_processor.CanBeginMemoryInstruction(in lease)) StartDataMemoryInstruction(instruction);
        else ScheduleCompletionAt(instruction, retirement);
        _automaticStagesStarted = true;
    }
}
