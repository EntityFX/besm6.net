using Besm6.Runtime.Timing;

namespace Besm6.Runtime.Modeling;

internal sealed partial class HardwareProcessorModel
{
    private ArithmeticStageController? _boundArithmetic;
    private bool _arithmeticBindingTransition;
    private HardwareEventToken _boundCompletionEvent;
    private ArithmeticCommandWord? _boundCommandWord;
    private bool _boundAutomaticCompletion;
    private ArithmeticCommandHandle _boundArithmeticCommand;
    private bool _boundOperandInitiallyReady;
    internal ArithmeticCommandWord? LastIssuedArithmeticCommand { get; private set; }

    /// <summary>
    /// UU formation uses the shared immediate resolver; memory control supplies
    /// its allocated BRUS slot. Optional automatic completion admits only the
    /// implemented fixed logical sequences, before taking CPU operand effects.
    /// </summary>
    internal ArithmeticCommandHandle BindArithmeticInstruction(HardwareInstructionHandle instruction,
        HardwareDuration cycle, bool operandReady, ArithmeticOperandBuffer? buffer = null,
        bool automaticCompletion = false, ArithmeticErrorPolicy? errorPolicy = null)
    {
        EnsureBindableArithmetic(in instruction);
        var opcode = instruction.Instruction!.Value.Opcode;
        byte operand = ArithmeticCommandEncoding.IsImmediate(opcode)
            ? _processor.GetPreparedImmediateOperand(_pendingInstruction!.Value) : (byte)0;
        return BindArithmeticInstruction(instruction, ArithmeticCommandEncoding.Encode(opcode, operand, buffer),
            cycle, operandReady, automaticCompletion, errorPolicy);
    }

    private void EnsureBindableArithmetic(in HardwareInstructionHandle instruction)
    {
        EnsureDriverAllowed();
        if (!IsCurrent(in instruction) || _completionReady || _cancellationRequested ||
            _boundArithmetic is not null || !_processor.CanCaptureArithmeticOperand(_pendingInstruction!.Value))
            throw new InvalidOperationException("No uncaptured arithmetic CPU lease can be bound.");
    }

    /// <summary>Register an explicit microsequence permission; stale/cancelled requests cannot execute it.</summary>
    internal HardwareEventToken ScheduleBoundArithmeticCompletionAt(HardwareInstructionHandle instruction,
        HardwareInstant time)
    {
        if (_boundAutomaticCompletion)
            throw new InvalidOperationException("The controller owns the automatic arithmetic completion deadline.");
        if (!IsCurrent(in instruction) || _boundArithmetic is not { } controller ||
            _completionReady || _cancellationRequested || _capturingOperand || _publishingInstruction)
            throw new InvalidOperationException("No waiting arithmetic binding owns this completion signal.");
        HardwareEventToken signal = default;
        signal = Timeline.ScheduleAt(time, () =>
        {
            if (_boundCompletionEvent == signal) _boundCompletionEvent = default;
            if (IsCurrent(in instruction) && !_cancellationRequested && ReferenceEquals(controller, _boundArithmetic))
                controller.CompleteOperation();
        });
        Timeline.Cancel(_boundCompletionEvent);
        _boundCompletionEvent = signal;
        return signal;
    }

    /// <summary>
    /// Attach the single shared CPU lease to АУ (арифметическое устройство).
    /// The caller still supplies the verified 17-bit command code, RPK and IZOP;
    /// this binding derives neither physical decoding nor operation deadlines.
    /// This legacy overload keeps Dubna delivery; typed binding may choose physical
    /// error control explicitly. Register synchronization is a logical boundary,
    /// not a hardware general-clear pulse.
    /// </summary>
    internal ArithmeticCommandHandle BindArithmeticInstruction(HardwareInstructionHandle instruction,
        uint code, HardwareDuration cycle, bool operandReady)
        => BindArithmeticInstructionCore(instruction, code, cycle, operandReady, ArithmeticOperandRoute.Buffered);

    /// <summary>Use the documented AU interface source; encoding from a guest instruction is a separate UU responsibility.</summary>
    internal ArithmeticCommandHandle BindArithmeticInstruction(HardwareInstructionHandle instruction,
        ArithmeticCommandWord command, HardwareDuration cycle, bool operandReady, bool automaticCompletion = false, ArithmeticErrorPolicy? errorPolicy = null)
    {
        EnsureBindableArithmetic(in instruction);
        var route = command.Source switch
        {
            ArithmeticCommandSource.Immediate => ArithmeticOperandRoute.Direct,
            ArithmeticCommandSource.BufferRead => ArithmeticOperandRoute.Buffered,
            _ => throw new ArgumentException("This arithmetic binding does not yet execute the selected AU input source.", nameof(command))
        };
        var opcode = instruction.Instruction!.Value.Opcode;
        if (command.OperationCode != ArithmeticCommandEncoding.OperationCode(opcode))
            throw new ArgumentException("The physical AU code does not match its shared CPU instruction.", nameof(command));
        if (route == ArithmeticOperandRoute.Direct)
        {
            if (!ArithmeticCommandEncoding.IsImmediate(opcode) ||
                command.ImmediateOperand != _processor.GetPreparedImmediateOperand(_pendingInstruction!.Value))
                throw new ArgumentException("The direct AU operand does not match the shared CPU address formation.", nameof(command));
        }
        else if (!Enum.IsDefined((ArithmeticOperandBufferKind)(command.Raw & 0x38)))
            throw new ArgumentException("Mixed AU buffer-class flags have no assigned control priority.", nameof(command));
        HardwareDuration? delay = automaticCompletion ? ArithmeticFixedCompletionTiming.Duration(opcode, cycle) : null;
        var handle = BindArithmeticInstructionCore(instruction, command.Raw, cycle, operandReady, route, delay, errorPolicy);
        _boundCommandWord = command;
        _boundAutomaticCompletion = automaticCompletion;
        LastIssuedArithmeticCommand = command;
        return handle;
    }

    private ArithmeticCommandHandle BindArithmeticInstructionCore(HardwareInstructionHandle instruction,
        uint code, HardwareDuration cycle, bool operandReady, ArithmeticOperandRoute route,
        HardwareDuration? completionDelay = null, ArithmeticErrorPolicy? errorPolicy = null)
    {
        EnsureBindableArithmetic(in instruction);
        if (errorPolicy.HasValue && _processor.Supervisor is null)
            throw new InvalidOperationException("Physical CPU arithmetic binding requires the supervisor profile.");
        bool immediate = instruction.Instruction!.Value.Opcode is Opcode.EPlusN or Opcode.EMinusN;
        if (immediate != (route == ArithmeticOperandRoute.Direct))
            throw new ArgumentException("The AU operand source does not match the shared CPU instruction.", nameof(route));
        var controller = CreateArithmeticController(cycle, errorPolicy);
        if (!controller.TryBind(this, code, () => SampleBoundOperand(instruction), operandReady,
            _processor.GetA(), _processor.GetY(), transition => AcceptBoundTransition(instruction, transition),
            out var command, route, completionDelay, () => IsCurrent(in instruction) && !_cancellationRequested))
            throw new InvalidOperationException("The idle arithmetic controller could not receive its CPU command.");
        _boundArithmetic = controller; _boundArithmeticCommand = command;
        _boundOperandInitiallyReady = operandReady;
        return command;
    }

    private PreparedArithmeticOperation? SampleBoundOperand(HardwareInstructionHandle instruction)
    {
        // A direct CPU reset may invalidate the lease before the driver is resumed.
        // Its stale transfer must not read memory or publish into the restarted CPU.
        if (!IsCurrent(in instruction) || _cancellationRequested) return null;
        if (_boundCommandWord is { Source: ArithmeticCommandSource.Immediate } command &&
            command.ImmediateOperand != _processor.GetPreparedImmediateOperand(_pendingInstruction!.Value))
            throw new InvalidOperationException("The issued immediate AU operand changed before acceptance.");
        _arithmeticBindingTransition = true;
        try
        {
            if (_timedOperandRequested)
            {
                var transfer = _timedOperandTransfer ?? throw new InvalidOperationException("The accepted memory operand is missing.");
                return CaptureTransferredOperand(instruction, transfer);
            }
            return CaptureArithmeticOperand(instruction);
        }
        finally { _arithmeticBindingTransition = false; }
    }

    private void AcceptBoundTransition(HardwareInstructionHandle instruction, ArithmeticStageTransition transition)
    {
        if (!IsCurrent(in instruction) || _cancellationRequested) return;
        _arithmeticBindingTransition = true;
        try
        {
            if (transition.Kind == ArithmeticStageTransitionKind.OperandRejected)
            {
                if (!TryIndicateCompletionReady(in instruction))
                    throw new InvalidOperationException("The rejected operand did not release its CPU fault boundary.");
            }
            else if (transition.Kind == ArithmeticStageTransitionKind.Completed)
            {
                // Overflow has an output and is delivered by the shared normalizer;
                // invalid division supplies its original cause instead of an output.
                var failure = transition.Fault?.Cause;
                var output = transition.Output;
                if (_boundArithmetic!.Errors is not null)
                {
                    if (failure is not null)
                    {
                        if (PendingArithmeticInterruption == 0)
                            throw new NotSupportedException("Invalid-divisor continuation with suppressed signals needs a verified physical result.");
                        _physicalArithmeticFailure = failure;
                        _completionReady = true;
                        return;
                    }
                    // AU error control already owns indication and delivery. Preserve
                    // the exact common A/Y/rounding, without raising a Dubna avost again.
                    if (output is { } result) output = result with { Overflow = false };
                }
                if (!TryIndicateArithmeticCompletion(instruction, failure is null ? output : null, failure))
                    throw new InvalidOperationException("Arithmetic completion lost its CPU execution owner.");
            }
        }
        finally { _arithmeticBindingTransition = false; }
    }

    private void ReleaseArithmeticBinding()
    {
        if (_boundArithmetic is not { } controller) return;
        controller.ReleaseBinding(this);
        Timeline.Cancel(_boundCompletionEvent);
        _boundCompletionEvent = default;
        _boundCommandWord = null;
        _boundAutomaticCompletion = false;
        _boundArithmetic = null;
    }
}
