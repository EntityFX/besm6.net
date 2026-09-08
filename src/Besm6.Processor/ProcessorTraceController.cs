namespace Besm6.Core
{
    /// <summary>
    /// Формирует типизированные снимки трассировки без файлового или консольного I/O.
    /// </summary>
    internal sealed class ProcessorTraceController
    {
        private readonly ProcessorState _state;
        private PendingTrace? _pending;
        private ulong _sequence;

        internal ProcessorTraceController(ProcessorState state)
        {
            _state = state;
        }

        internal Action<InstructionTraceRecord>? InstructionTrace { get; set; }
        internal Action<RegisterTraceRecord>? RegisterTrace { get; set; }

        internal void Reset()
        {
            _pending = null;
            _sequence = 0;
        }

        internal void Begin(Word48 rawWord, uint rawInstruction, DecodedInstruction instruction)
        {
            if (InstructionTrace is null && RegisterTrace is null)
            {
                _pending = null;
                return;
            }

            _pending = new PendingTrace(rawWord, rawInstruction, instruction, new ProcessorSnapshot(_state));
        }

        internal void Complete()
        {
            if (_pending is not PendingTrace pending)
                return;

            _pending = null;
            var after = new ProcessorSnapshot(_state);
            InstructionTrace?.Invoke(new InstructionTraceRecord(
                _sequence++,
                pending.RawWord,
                pending.RawInstruction,
                pending.Instruction,
                pending.Before,
                after));
            EmitRegisterChanges(pending.Before, after);
        }

        private void EmitRegisterChanges(ProcessorSnapshot before, ProcessorSnapshot after)
        {
            Action<RegisterTraceRecord>? sink = RegisterTrace;
            if (sink is null)
                return;

            if (after.A != before.A) sink(new RegisterTraceRecord("A", after.A.Value));
            if (after.Y != before.Y) sink(new RegisterTraceRecord("Y", after.Y.Value));
            for (int index = 0; index < ArchitectureConstants.IndexRegCount; index++)
            {
                if (after.M[index] != before.M[index])
                    sink(new RegisterTraceRecord("M" + Convert.ToString(index, 8), after.M[index]));
            }
            if (after.R != before.R) sink(new RegisterTraceRecord("R", after.R));
            if (after.ApplyC != before.ApplyC)
                sink(new RegisterTraceRecord(after.ApplyC ? "C" : "CLEARC", after.C));
        }

        private readonly record struct PendingTrace(
            Word48 RawWord,
            uint RawInstruction,
            DecodedInstruction Instruction,
            ProcessorSnapshot Before);
    }
}
