using Besm6.Runtime.Timing;

namespace Besm6.Runtime.Modeling;

internal sealed partial class HardwareProcessorModel
{
    private ArithmeticStageController? _boundArithmetic;
    private bool _arithmeticBindingTransition;
    private HardwareEventToken _boundCompletionEvent;

    /// <summary>Register an explicit microsequence permission; stale/cancelled requests cannot execute it.</summary>
    internal HardwareEventToken ScheduleBoundArithmeticCompletionAt(HardwareInstructionHandle instruction,
        HardwareInstant time)
    {
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
    /// Dubna-compatible data policy is used until physical interruption delivery
    /// is integrated. Register synchronization is a logical execution boundary,
    /// not a hardware general-clear pulse.
    /// </summary>
    internal ArithmeticCommandHandle BindArithmeticInstruction(HardwareInstructionHandle instruction,
        uint code, HardwareDuration cycle, bool operandReady)
        => BindArithmeticInstructionCore(instruction, code, cycle, operandReady, ArithmeticOperandRoute.Buffered);

    /// <summary>Use the documented AU interface source; encoding from a guest instruction is a separate UU responsibility.</summary>
    internal ArithmeticCommandHandle BindArithmeticInstruction(HardwareInstructionHandle instruction,
        ArithmeticCommandWord command, HardwareDuration cycle, bool operandReady)
    {
        var route = command.Source switch
        {
            ArithmeticCommandSource.Immediate => ArithmeticOperandRoute.Direct,
            ArithmeticCommandSource.BufferRead => ArithmeticOperandRoute.Buffered,
            _ => throw new ArgumentException("This arithmetic binding does not yet execute the selected AU input source.", nameof(command))
        };
        return BindArithmeticInstructionCore(instruction, command.Raw, cycle, operandReady, route);
    }

    private ArithmeticCommandHandle BindArithmeticInstructionCore(HardwareInstructionHandle instruction,
        uint code, HardwareDuration cycle, bool operandReady, ArithmeticOperandRoute route)
    {
        EnsureDriverAllowed();
        if (!IsCurrent(in instruction) || _completionReady || _cancellationRequested ||
            _boundArithmetic is not null || !_processor.CanCaptureArithmeticOperand(_pendingInstruction!.Value))
            throw new InvalidOperationException("No uncaptured arithmetic CPU lease can be bound.");
        bool immediate = instruction.Instruction!.Value.Opcode is Opcode.EPlusN or Opcode.EMinusN;
        if (immediate != (route == ArithmeticOperandRoute.Direct))
            throw new ArgumentException("The AU operand source does not match the shared CPU instruction.", nameof(route));
        var controller = CreateArithmeticController(cycle);
        if (!controller.TryBind(this, code, () => SampleBoundOperand(instruction), operandReady,
            _processor.GetA(), _processor.GetY(), transition => AcceptBoundTransition(instruction, transition), out var command, route))
            throw new InvalidOperationException("The idle arithmetic controller could not receive its CPU command.");
        _boundArithmetic = controller;
        return command;
    }

    private PreparedArithmeticOperation? SampleBoundOperand(HardwareInstructionHandle instruction)
    {
        // A direct CPU reset may invalidate the lease before the driver is resumed.
        // Its stale transfer must not read memory or publish into the restarted CPU.
        if (!IsCurrent(in instruction) || _cancellationRequested) return null;
        _arithmeticBindingTransition = true;
        try { return CaptureArithmeticOperand(instruction); }
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
                if (!TryIndicateArithmeticCompletion(instruction, failure is null ? transition.Output : null, failure))
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
        _boundArithmetic = null;
    }
}
