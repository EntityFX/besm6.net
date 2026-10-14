using Besm6.Architecture.Debug;
using Besm6.Architecture.Isa;
using Besm6.Architecture.Memory;
using Besm6.Architecture.Numerics;
using Besm6.Architecture.Supervisor;

namespace Besm6.Architecture.Machine
{
    public interface IProcessor : IRegisterOps, IAlu, IProcessorDebug
    {
        IProcessorState State { get; }
        bool RightInstruction { get; }


        /// <summary>Completed opcode notification without allocating register snapshots.</summary>
        Action<Opcode>? InstructionExecuted { get; set; }

        /// <summary>Cache decoded halves while verifying the fetched bits on every instruction.</summary>
        bool InstructionCacheEnabled { get; set; }


        /// <summary>Обработчик полного контекста вызова экстракода.</summary>
        Func<ExtracodeCall, bool>? ExtracodeDispatch { get; set; }

        ProcessorProfile Profile { get; }
        ISupervisorControl? Supervisor { get; }

        bool LastStepCompleted { get; set; }

        bool ExecutionProhibited { get; set; }
        bool PreparedExecutionOnly { get; set; }

        /// <summary>
        /// Счётчик перехвата (intercept_count): 0 — перехват отключён,
        /// 1 — перехватить следующую ошибку арифметики (overflow/div-by-zero).
        /// </summary>
        int InterceptCount { get; set; }
        /// <summary>Потребить перехват (после срабатывания ошибки).</summary>
        void ConsumeIntercept();
        uint InterceptAddr { get; set; }

        void Reset();
        void StartAt(uint address);

        void StackCorrection();

        bool Step();
    }
}