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

            ulong rawWord = _memory.MemFetch(_state.K);
            uint rawInstruction = _state.IsRightHalf
                ? (uint)rawWord
                : (uint)(rawWord >> 24);
            rawInstruction &= 0xFF_FFFFu;
            _state.RawInstruction = rawInstruction;

            DecodedInstruction instruction = InstructionCodec.DecodeHalf(rawInstruction);
            uint opcode = (uint)instruction.Opcode;
            if (!_state.IsRightHalf && _processor.DebugCheckFetch(_state.K, opcode))
                return false;

            _processor.TraceInstruction?.Invoke(
                _state.K,
                _state.IsRightHalf,
                rawInstruction,
                opcode);
            _processor.CanonPre(
                _state.K,
                _state.IsRightHalf,
                rawWord,
                rawInstruction,
                opcode,
                instruction.Register,
                instruction.Address);

            var frame = new ExecutionFrame
            {
                Instruction = instruction,
                RawInstruction = rawInstruction,
                RawWord = rawWord,
                Address = instruction.Address,
                EffectiveAddress = _state.EffectiveAddress,
                WasRightHalf = _state.IsRightHalf,
                NextK = ArchitectureConstants.NormalizeAddress(_state.K + 1),
                A = _state.A.Value,
                Y = _state.Y.Value,
            };

            AdvanceInstructionHalf();
            if (_state.ApplyC)
                frame.Address = ArchitectureConstants.NormalizeAddress(frame.Address + _state.C);

            InstructionOutcome outcome;
            try
            {
                outcome = Dispatch(frame);
            }
            catch (ProcessorException exception) when (string.IsNullOrEmpty(exception.Message))
            {
                FinalizeInstruction(frame, updateRegistersAndModification: false);
                throw;
            }

            FinalizeInstruction(frame, updateRegistersAndModification: true);
            return outcome == InstructionOutcome.Stop;
        }

        private InstructionOutcome Dispatch(ExecutionFrame frame)
        {
            uint opcode = (uint)frame.Instruction.Opcode;
            if (opcode <= (uint)Opcode.Ntr)
                return _memoryInstructions.Execute(frame);
            if (opcode is >= (uint)Opcode.Ati and <= (uint)Opcode.Op47)
                return _indexInstructions.Execute(frame);
            if (opcode >= (uint)Opcode.Utc)
                return _controlInstructions.Execute(frame);
            if (ExtracodeInstructionExecutor.CanExecute(frame.Instruction.Opcode))
                return _extracodeInstructions.Execute(frame);
            throw new ProcessorException($"Unknown instruction {opcode}");
        }

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

        private void FinalizeInstruction(ExecutionFrame frame, bool updateRegistersAndModification)
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

                _state.A = Word48.FromInt48(frame.A);
                _state.Y = Word48.FromInt48(frame.Y);
            }
            _processor.CanonPost(_state.K, _state.IsRightHalf);
        }
    }
}
