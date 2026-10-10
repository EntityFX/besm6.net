using Besm6.Runtime.Timing;

namespace Besm6.Runtime.Modeling;

internal sealed partial class HardwareProcessorModel
{
    internal ulong PendingArithmeticInterruption => _arithmeticInterruptions?.PendingCause ?? 0;
    internal ulong ReceivedArithmeticInterruptions => _arithmeticInterruptions?.ReceivedSamples ?? 0;
    internal ArithmeticInterruptionSample? LastArithmeticInterruption => _arithmeticInterruptions?.LastSample;

    /// <summary>
    /// УУ — устройство управления. Accept OpPr1 outside calendar callbacks.
    /// TO-8 §6.20 does not preserve the exact failed AU command address: the UU
    /// supplies its return position/flags. This method does not infer overlap from K.
    /// AU signals and CPU-reset lifetime are independent of this pending delivery.
    /// </summary>
    internal bool AcceptArithmeticInterruption(uint returnWord, uint savedFlags)
    {
        const uint admittedFlags = SupervisorControl.SavedRightHalf | SupervisorControl.SavedNextInstruction | 0x30u;
        if ((savedFlags & ~admittedFlags) != 0) throw new ArgumentOutOfRangeException(nameof(savedFlags));
        var supervisor = _processor.Supervisor ?? throw new InvalidOperationException("Arithmetic UU acceptance requires the supervisor profile.");
        BeginDriving();
        try
        {
            RetireInvalidatedPreparation();
            if (_pendingInstruction.HasValue || _processor.HasPreparedInstruction)
                throw new InvalidOperationException("Arithmetic interruption acceptance cannot replace a pending CPU instruction.");
            Timeline.AdvanceTo(Timeline.Now);
            ulong cause = PendingArithmeticInterruption;
            if (cause == 0) return false;
            // Relatch after software ResetCpu; it does not clear the hardware source.
            supervisor.LatchInternalCause(cause);
            supervisor.AcceptInternalInterrupt(cause, returnWord, savedFlags);
            _arithmeticInterruptions!.Acknowledge();
            return true;
        }
        finally { EndDriving(); }
    }
}
