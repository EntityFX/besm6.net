namespace Besm6.Core
{
    /// <summary>Подготавливает typed-вызов экстракода, не реализуя host services.</summary>
    internal sealed class ExtracodeInstructionExecutor
    {
        private readonly ProcessorState _state;

        internal ExtracodeInstructionExecutor(ProcessorState state)
        {
            _state = state;
        }

        internal Func<ExtracodeCall, bool>? Dispatch { get; set; }

        internal InstructionOutcome Execute(ref ExecutionFrame frame)
        {
            int reg = frame.Instruction.Register;
            uint effectiveAddress = ArchitectureConstants.NormalizeAddress(
                frame.Address + _state.M[reg]);
            frame.EffectiveAddress = effectiveAddress;
            _state.EffectiveAddress = effectiveAddress;
            _state.M[14] = effectiveAddress;
            bool wasRightHalf = !_state.IsRightHalf; // The instruction half was advanced before dispatch.

            if (_state.IsRightHalf)
            {
                _state.K = ArchitectureConstants.NormalizeAddress(_state.K + 1);
                _state.IsRightHalf = false;
            }

            var call = new ExtracodeCall(
                (Extracode)(int)frame.Instruction.Opcode,
                effectiveAddress,
                frame.Instruction.Register,
                frame.Instruction.Address,
                wasRightHalf);

            bool handled = Dispatch is not null && Dispatch(call);
            if (!handled)
                throw new ProcessorException($"Extracode {(int)frame.Instruction.Opcode} not implemented");

            frame.RegistersInState = true;
            _state.SetLogical();
            return InstructionOutcome.Continue;
        }

        internal static bool CanExecute(Opcode opcode)
        {
            uint value = (uint)opcode;
            return value is >= 0x28 and <= 0x3F or 0x80 or 0x88;
        }
    }
}
