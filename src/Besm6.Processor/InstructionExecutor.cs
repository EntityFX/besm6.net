namespace Besm6.Core
{
    /// <summary>
    /// Единый конвейер fetch/decode/dispatch/finalize для одного полуслова БЭСМ-6.
    /// </summary>
    public sealed class InstructionExecutor
    {
        private readonly Processor _processor;
        private readonly ProcessorState _state;
        private readonly ProcessorMemoryAccess _memory;
        private readonly MemoryInstructionExecutor _memoryInstructions;
        private readonly IndexInstructionExecutor _indexInstructions;
        private readonly ControlInstructionExecutor _controlInstructions;
        private readonly ExtracodeInstructionExecutor _extracodeInstructions;
        private const int DecodeCacheSize = 65536;
        private CachedInstruction[]? _decodeCache;

        internal bool InstructionCacheEnabled
        {
            get => _decodeCache is not null;
            set => _decodeCache = value ? _decodeCache ?? new CachedInstruction[DecodeCacheSize] : null;
        }

        private struct CachedInstruction
        {
            internal uint Tag;
            internal DecodedInstruction Instruction;
        }

        internal InstructionExecutor(
            Processor processor,
            ProcessorState state,
            ProcessorMemoryAccess memory,
            Alu alu)
        {
            _processor = processor;
            _state = state;
            _memory = memory;
            _memoryInstructions = new MemoryInstructionExecutor(state, memory, alu);
            _indexInstructions = new IndexInstructionExecutor(state, memory);
            _controlInstructions = new ControlInstructionExecutor(state, memory);
            _extracodeInstructions = new ExtracodeInstructionExecutor(state);
        }

        internal Func<ExtracodeCall, bool>? ExtracodeDispatch
        {
            get => _extracodeInstructions.Dispatch;
            set => _extracodeInstructions.Dispatch = value;
        }

        /// <summary>Выполняет одну инструкцию; true означает команду STOP.</summary>
        public bool Execute()
        {
            try
            {
                return ExecuteCore();
            }
            catch (Processor.DebugWatchAbortException)
            {
                return false;
            }
        }

        private bool ExecuteCore()
        {
            _state.StackCorrection = 0;
            _state.K = ArchitectureConstants.NormalizeAddress(_state.K);

            ulong rawWord = _memory.MemFetch(_state.K, _state.IsRightHalf);
            uint rawInstruction = _state.IsRightHalf
                ? (uint)rawWord
                : (uint)(rawWord >> 24);
            rawInstruction &= 0xFF_FFFFu;
            _state.RawInstruction = rawInstruction;

            DecodedInstruction instruction;
            if (_decodeCache is { } cache)
            {
                // Fetch still occurs for every half. Compare the actual bits, so writes
                // through any memory interface (including the other half) take effect.
                uint index = (_state.K << 1) | (_state.IsRightHalf ? 1u : 0u);
                ref CachedInstruction entry = ref cache[index];
                uint tag = rawInstruction + 1; // Zero is a valid instruction, not an empty entry.
                if (entry.Tag != tag)
                {
                    entry.Instruction = InstructionCodec.DecodeHalf(rawInstruction);
                    entry.Tag = tag;
                }
                instruction = entry.Instruction;
            }
            else
                instruction = InstructionCodec.DecodeHalf(rawInstruction);
            uint opcode = (uint)instruction.Opcode;
            if (_state.DebugFetchArmed && !_state.IsRightHalf && _processor.DebugCheckFetch(_state.K, opcode))
                return false;

            _processor.TraceInstruction?.Invoke(
                _state.K,
                _state.IsRightHalf,
                rawInstruction,
                opcode);
            _processor.CanonPre(rawWord, rawInstruction, instruction);

            var frame = new ExecutionFrame
            {
                Instruction = instruction,
                Address = instruction.Address,
                EffectiveAddress = _state.EffectiveAddress,
                A = _state.A.Value,
                Y = _state.Y.Value,
            };

            AdvanceInstructionHalf();
            if (_state.ApplyC)
                frame.Address = ArchitectureConstants.NormalizeAddress(frame.Address + _state.C);

            InstructionOutcome outcome;
            try
            {
                outcome = Dispatch(ref frame);
            }
            catch (ProcessorException exception) when (string.IsNullOrEmpty(exception.Message))
            {
                FinalizeInstruction(ref frame, updateRegistersAndModification: false);
                throw;
            }

            FinalizeInstruction(ref frame, updateRegistersAndModification: true);
            return outcome == InstructionOutcome.Stop;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private InstructionOutcome Dispatch(ref ExecutionFrame frame)
        {
            uint opcode = (uint)frame.Instruction.Opcode;
            if (opcode <= (uint)Opcode.Ntr)
                return _memoryInstructions.Execute(ref frame);
            if (opcode is >= (uint)Opcode.Ati and <= (uint)Opcode.Op47)
                return _indexInstructions.Execute(ref frame);
            if (opcode >= (uint)Opcode.Utc)
                return _controlInstructions.Execute(ref frame);
            if (ExtracodeInstructionExecutor.CanExecute(frame.Instruction.Opcode))
                return _extracodeInstructions.Execute(ref frame);
            return ThrowUnknownInstruction(opcode);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static InstructionOutcome ThrowUnknownInstruction(uint opcode) =>
            throw new ProcessorException($"Unknown instruction {opcode}");

        /// <summary>
        /// Run ordinary instructions without per-step runtime/CPU wrappers.
        /// Stop before an extracode: it can install hooks and must retain the
        /// observable Step completion order in MachineCore.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
        internal bool ExecuteUnobservedBlock(int count, ref long completed, ref ulong tick)
        {
            var state = _state;
            var cache = _decodeCache;
            for (int i = 0; i < count; i++)
            {
                state.StackCorrection = 0;
                state.K = ArchitectureConstants.NormalizeAddress(state.K);
                ulong rawWord = _memory.MemFetch(state.K, state.IsRightHalf);
                uint raw = (state.IsRightHalf ? (uint)rawWord : (uint)(rawWord >> 24)) & 0xFF_FFFFu;
                DecodedInstruction instruction;
                if (cache is not null)
                {
                    ref CachedInstruction entry = ref cache[(state.K << 1) | (state.IsRightHalf ? 1u : 0u)];
                    uint tag = raw + 1;
                    if (entry.Tag != tag)
                    {
                        entry.Instruction = InstructionCodec.DecodeHalf(raw);
                        entry.Tag = tag;
                    }
                    instruction = entry.Instruction;
                }
                else
                    instruction = InstructionCodec.DecodeHalf(raw);

                // No instruction has started yet. Execute the callback-capable
                // command using the ordinary path, including its fetch.
                if (ExtracodeInstructionExecutor.CanExecute(instruction.Opcode))
                    return false;

                state.RawInstruction = raw;
                var frame = new ExecutionFrame
                {
                    Instruction = instruction,
                    Address = instruction.Address,
                    EffectiveAddress = state.EffectiveAddress,
                    A = state.A.Value,
                    Y = state.Y.Value,
                };
                AdvanceInstructionHalf();
                if (state.ApplyC)
                    frame.Address = ArchitectureConstants.NormalizeAddress(frame.Address + state.C);
                InstructionOutcome outcome = Dispatch(ref frame);
                FinalizeInstruction(ref frame, updateRegistersAndModification: true, observe: false);
                tick++;
                completed++;
                if (outcome == InstructionOutcome.Stop) return true;
            }
            return false;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void AdvanceInstructionHalf()
        {
            if (_state.IsRightHalf)
            {
                _state.K = ArchitectureConstants.NormalizeAddress(_state.K + 1);
                _state.IsRightHalf = false;
            }
            else
            {
                _state.IsRightHalf = true;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void FinalizeInstruction(ref ExecutionFrame frame, bool updateRegistersAndModification, bool observe = true)
        {
            if (updateRegistersAndModification)
            {
                if (frame.UpdateModificationRegister)
                {
                    if (frame.NextC != 0)
                    {
                        _state.C = frame.NextC;
                        _state.ApplyC = true;
                    }
                    else
                    {
                        _state.ApplyC = false;
                    }
                }

                if (!frame.RegistersInState)
                {
                    _state.A = Word48.FromInt48(frame.A);
                    _state.Y = Word48.FromInt48(frame.Y);
                }
            }
            if (observe) _processor.CanonPost(_state.K, _state.IsRightHalf);
        }
    }
}
