using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;

namespace Besm6.Architecture.Debug
{
    /// <summary>
    /// Отладка и трассировка процессора
    /// </summary>
    public interface IProcessorDebug
    {
        /// <summary>Типизированная трассировка изменений регистров.</summary>
        Action<RegisterTraceRecord>? RegisterTrace { get; set; }

        /// <summary>
        /// Вызывается в НАЧАЛЕ инструкции: после fetch RK и decode
        /// Аргументы: (k, rightFlag, rk, opcode). null = выключен.
        /// </summary>
        Action<uint, bool, uint, uint>? TraceInstruction { get; set; }

        /// <summary>Типизированная трассировка исполненных инструкций.</summary>
        Action<InstructionTraceRecord>? InstructionTrace { get; set; }

        IProcessorInstructionAccounting? InstructionAccounting { get; }

        IProcessorInstructionAccounting BeginInstructionAccounting(ReadOnlySpan<int> cycles, int defaultCycles);

        void EndInstructionAccounting(IProcessorInstructionAccounting session);

        void CancelInstructionTrace();

        void CanonPre(ulong word, uint rk, DecodedInstruction instruction);

        void CanonPost(uint k, bool right);

        bool DebugCheckFetch(uint addr, uint opcode);

        bool DebugCheckMemory(uint addr, uint mode);
        void ArmDebugWatch(uint xfer, bool printInfo, uint mode, uint watch, uint cont);
    }
}
