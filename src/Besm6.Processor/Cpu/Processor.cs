using Besm6.Architecture.Debug;
using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;
using Besm6.Architecture.Memory;
using Besm6.Architecture.Numerics;
using Besm6.Architecture.Supervisor;
using Besm6.Core;
using Besm6.Processor.Arithmetic;
using Besm6.Processor.Diagnostics;
using Besm6.Processor.Execution;
using Besm6.Processor.Supervisor;
using System;

namespace Besm6.Processor.Cpu
{
    /// <summary>
    /// Процессор БЭСМ-6
    /// </summary>
    public partial class Processor : IRegisterOps, IAlu, IProcessor, IProcessorDebug, IInstructionAccountingSource
    {
        private readonly IMutableProcessorState _state;
        private readonly ProcessorTraceController _traceController;

        public IProcessorState State { get; }
        internal IMutableProcessorState MutableState => _state;

        private readonly ProcessorDebugWatch _debugWatch;
        private readonly ProcessorMemoryAccess _memoryAccess;
        internal readonly IAlu _alu;
        internal readonly InstructionExecutor _executor;
        public IProcessorInstructionAccounting? InstructionAccounting { get; private set; }

        public void PublishPreparedResult(NormalizedArithmeticResult result) => _alu.PublishPreparedResult(result);

        public IProcessorInstructionAccounting BeginInstructionAccounting(ReadOnlySpan<int> cycles, int defaultCycles)
        {
            if (InstructionAccounting is not null)
                throw new InvalidOperationException("A CPU accounting session is already active.");
            return InstructionAccounting = new ProcessorInstructionAccounting(cycles, defaultCycles);
        }

        public void EndInstructionAccounting(IProcessorInstructionAccounting session)
        {
            if (!ReferenceEquals(InstructionAccounting, session))
                throw new InvalidOperationException("The accounting session belongs to another run.");
            session.CancelPending();
            InstructionAccounting = null;
        }

        /// <summary>
        /// (ref/trace.cpp:240). Вызывается в НАЧАЛЕ инструкции: после fetch RK и decode
        /// (reg/addr/opcode), НО до advance K и до исполнения (ref/processor.cpp:151).
        /// Аргументы: (k, rightFlag, rk, opcode). null = выключен.
        /// </summary>
        public Action<uint, bool, uint, uint>? TraceInstruction { get; set; }

        /// <summary>Типизированная трассировка исполненных инструкций.</summary>
        public Action<InstructionTraceRecord>? InstructionTrace
        {
            get => _traceController.InstructionTrace;
            set => _traceController.InstructionTrace = value;
        }

        /// <summary>Completed opcode notification without allocating register snapshots.</summary>
        public Action<Opcode>? InstructionExecuted
        {
            get => _traceController.InstructionExecuted;
            set => _traceController.InstructionExecuted = value;
        }

        /// <summary>Cache decoded halves while verifying the fetched bits on every instruction.</summary>
        public bool InstructionCacheEnabled
        {
            get => _executor.InstructionCacheEnabled;
            set => _executor.InstructionCacheEnabled = value;
        }

        /// <summary>Типизированная трассировка изменений регистров.</summary>
        public Action<RegisterTraceRecord>? RegisterTrace
        {
            get => _traceController.RegisterTrace;
            set => _traceController.RegisterTrace = value;
        }

        /// <summary>Обработчик полного контекста вызова экстракода.</summary>
        public Func<ExtracodeCall, bool>? ExtracodeDispatch
        {
            get => _executor.ExtracodeDispatch;
            set => _executor.ExtracodeDispatch = value;
        }

        public Processor(IOperandMemory memory) : this(memory, ProcessorProfile.Dubna) { }

        public ProcessorProfile Profile { get; }
        public ISupervisorControl? Supervisor { get; }
        public bool LastStepCompleted { get; internal set; } = true;

        public Processor(IOperandMemory memory, ProcessorProfile profile)
        {
            if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(profile));
            if (profile == ProcessorProfile.Supervisor && memory is not MappedMemoryBackend)
                throw new ArgumentException("Supervisor execution requires mapped memory.", nameof(memory));
            Profile = profile;
            _state = new ProcessorState();
            State = new ProcessorStateView(_state);
            if (profile == ProcessorProfile.Supervisor)
            {
                ((MappedMemoryBackend)memory).EnableSupervisorWritePublication();
                Supervisor = new SupervisorControl(_state, (MappedMemoryBackend)memory);
            }
            _traceController = new ProcessorTraceController(_state);
            _debugWatch = new ProcessorDebugWatch(this, _state);
            _memoryAccess = new ProcessorMemoryAccess(_debugWatch, memory, profile == ProcessorProfile.Supervisor);
            _alu = new Alu(_state);
            _executor = new InstructionExecutor(this, _state, _memoryAccess, _alu);
            Reset();
        }


        public void Reset()
        {
            _executor.InvalidatePreparedInstruction();
            _state.Reset();
            _traceController.Reset();
            InstructionAccounting?.CancelPending();
            _debugWatch.Reset();
            Supervisor?.ResetCpu();
            LastStepCompleted = true;
        }

        #region Доступ к регистрам (для тестов)

        /// <summary>K — счётчик команд.</summary>
        public uint K { get => _state.K; set => _state.K = value; }
        /// <summary>A — аккумулятор.</summary>
        public Word48 A { get => _state.A; set => _state.A = value; }
        /// <summary>Y — регистр младших разрядов.</summary>
        public Word48 Y { get => _state.Y; set => _state.Y = value; }
        /// <summary>R — регистр режима арифметического устройства.</summary>
        public uint R { get => _state.R; set => _state.R = value; }
        /// <summary>M — индексные регистры.</summary>
        public ReadOnlySpan<uint> M => _state.M;
        /// <summary>C — регистр модификации адреса.</summary>
        public uint C { get => _state.C; set => _state.C = value; }

        public uint EffectiveAddress { get => _state.EffectiveAddress; set => _state.EffectiveAddress = value; }


        /// <summary>Признак применения регистра C к следующей инструкции.</summary>
        public bool ApplyC { get => _state.ApplyC; set => _state.ApplyC = value; }

        /// <summary>Человекочитаемый режим регистра R.</summary>
        public string RMode => IsLogical() ? "LOG" : (IsMultiplicative() ? "MUL" : "ADD");
        /// <summary>Сигнальный флаг правого полу-слова.</summary>
        public bool RightInstruction => _state.IsRightHalf;
        /// <summary>
        /// Счётчик перехвата (intercept_count): 0 — перехват отключён,
        /// 1 — перехватить следующую ошибку арифметики (overflow/div-by-zero).
        /// </summary>
        public int InterceptCount { get => _state.InterceptCount; set => _state.InterceptCount = value; }
        /// <summary>Потребить перехват (после срабатывания ошибки).</summary>
        public void ConsumeIntercept() => _state.InterceptCount = 0;
        public uint InterceptAddr { get => _state.InterceptAddress; set => _state.InterceptAddress = value; }

        /// <summary>
        /// Перехват арифметической ошибки (overflow / div-zero). Точный порт
        /// если перехват вооружён (InterceptCount>0) и сообщение — "Arithmetic overflow"
        /// или "Division by zero", то InterceptCount--, K=InterceptAddr,
        /// right_instr_flag=false, apply_c_reg=false, C=0, вернуть true.
        /// Иначе вернуть false (перехват отключён — ошибка не перехватывается).
        /// </summary>
        public bool Intercept(string message)
        {
            if (_state.InterceptCount > 0 &&
                (message == "Arithmetic overflow" || message == "Division by zero"))
            {
                _state.InterceptCount--;
                _state.K = _state.InterceptAddress & 0x7FFF;
                _state.IsRightHalf = false;
                _state.ApplyC = false;
                _state.C = 0;
                return true;
            }
            return false;
        }

        /// <summary>
        /// core.M[017] += corr_stack; corr_stack = 0.
        /// corr_stack выставляется инструкциями, предварительно изменившими M[017]
        /// (сл/вч/.../стx: +1, счм: -1, уим/мод(стек): +1) и сбрасывается в начале
        /// недоисполненное изменение стека.
        /// </summary>
        public void StackCorrection()
        {
            _state.M[15] = (uint)(_state.M[15] + _state.StackCorrection);
            _state.StackCorrection = 0;
        }

        /// <summary>Starts at the left instruction of a word, preserving other registers.</summary>
        public void StartAt(uint address)
        {
            _state.K = ArchitectureConstants.NormalizeAddress(address);
            _state.IsRightHalf = false;
        }

        /// <summary>Устанавливает регистр режима R.</summary>
        public void SetR(ulong val) => R = (uint)val;
        /// <summary>Устанавливает аккумулятор A.</summary>
        public void SetA(ulong val) => _state.A = Word48.FromInt48(val & ArchitectureConstants.BITS48);
        /// <summary>Устанавливает регистр младших разрядов Y.</summary>
        public void SetY(ulong val) => _state.Y = Word48.FromInt48(val & ArchitectureConstants.BITS48);

        /// <summary>Устанавливает индексный регистр M с указанным номером.</summary>
        public void SetM(int index, uint val) => _state.M[index & 0xF] = val;

        /// <summary>Возвращает индексный регистр M с указанным номером.</summary>
        public uint GetM(int index) => _state.M[index & 0xF];

        public void ArmDebugWatch(uint xfer, bool printInfo, uint mode, uint watch, uint cont)
            => _debugWatch.ArmDebugWatch(xfer, printInfo, mode, watch, cont);

        internal bool DebugCheckFetch(uint addr, uint opcode)
            => _debugWatch.DebugCheckFetch(addr, opcode);

        internal bool DebugCheckMemory(uint addr, uint mode)
            => _debugWatch.DebugCheckMemory(addr, mode);

        #endregion

        #region Арифметика АЛУ (делегирование в Alu)

        public void Add(Word48 val, bool negateA, bool negateVal) => _alu.Add(val, negateA, negateVal);
        public void AddExponent(int val) => _alu.AddExponent(val);
        public void ChangeSign(bool negateA) => _alu.ChangeSign(negateA);
        public void Multiply(Word48 val) => _alu.Multiply(val);
        public void Divide(Word48 val) => _alu.Divide(val);
        public void Shift(int nbits) => _alu.Shift(nbits);

        public void NormalizeAndRound(MantissaExponent a, ulong y, bool roundFlag)
        {
            _alu.NormalizeAndRound(a, y, roundFlag);
        }

        #endregion


        #region Режим АЛУ

        internal bool IsAdditive() => _state.IsAdditive;
        internal bool IsMultiplicative() => _state.IsMultiplicative;
        internal bool IsLogical() => _state.IsLogical;

        internal void SetAdditive() => _state.SetAdditive();
        internal void SetMultiplicative() => _state.SetMultiplicative();
        internal void SetLogical() => _state.SetLogical();

        #endregion

        #region Память

        internal ulong MemFetch(ulong addr) => _memoryAccess.MemFetch(addr, _state.IsRightHalf);

        internal ulong MemLoad(uint addr) => _memoryAccess.MemLoad(addr);

        internal void MemStore(uint addr, ulong val) => _memoryAccess.MemStore(addr, val);

        #endregion

        /// <summary>
        /// Выполняет одну инструкцию (левую или правую половину слова).
        /// Возвращает true, когда процессор остановлен (инструкция СТОП).
        /// </summary>
        internal bool ExecutionProhibited { get; set; }
        internal bool PreparedExecutionOnly { get; set; }

        public bool Step()
        {
            if (ExecutionProhibited || PreparedExecutionOnly)
                throw new InvalidOperationException("Direct CPU execution is unavailable while a callback or staged execution owns the machine.");
            return _executor.Execute();
        }

        internal bool CanExecuteUnobservedBlock =>
            !_executor.HasPreparedInstruction &&
            _memoryAccess.IsPlainCoreMemory && TraceInstruction is null &&
            !_traceController.IsEnabled && !_traceController.HasPending &&
            InstructionAccounting?.HasPending != true &&
            !_state.DebugState.DebugFetchArmed && !_state.DebugState.DebugMemoryArmed;

        internal bool ExecuteUnobservedBlock(int count, ref long completed, ref ulong tick) =>
            _executor.ExecuteUnobservedBlock(count, ref completed, ref tick);

        internal PreparedInstruction PrepareInstruction() => _executor.PrepareInstruction();
        internal PreparedInstruction BeginInstructionFetch() => _executor.BeginInstructionFetch();
        internal PreparedInstruction AcceptInstructionFetch(in PreparedInstruction lease,
            Word48 word, Exception? failure) => _executor.AcceptInstructionFetch(in lease, word, failure);
        internal uint GetPreparedMemoryOperandAddress(in PreparedInstruction instruction) =>
            _executor.GetPreparedMemoryOperandAddress(in instruction);
        internal PreparedArithmeticOperation? CaptureTransferredArithmeticOperand(in PreparedInstruction instruction,
            uint address, Word48 word, Exception? failure) => _executor.CaptureArithmeticOperand(in instruction, address, word, failure);
        internal bool IsPreparedInstructionSuppressed(in PreparedInstruction instruction) =>
            _executor.IsPreparedInstructionSuppressed(in instruction);
        internal bool CanBeginMemoryInstruction(in PreparedInstruction instruction) =>
            _executor.CanBeginMemoryInstruction(in instruction);
        internal bool CanCaptureArithmeticOperand(in PreparedInstruction instruction) =>
            _executor.CanCaptureArithmeticOperand(in instruction);
        internal CpuMemoryTransfer? BeginMemoryInstruction(in PreparedInstruction instruction) =>
            _executor.BeginMemoryInstruction(in instruction);
        internal CpuMemoryTransfer? AcceptMemoryTransfer(in PreparedInstruction instruction, Word48 word, Exception? failure) =>
            _executor.AcceptMemoryTransfer(in instruction, word, failure);
        internal bool RequiresMemoryTransfer(in PreparedInstruction instruction) =>
            _executor.RequiresMemoryTransfer(in instruction);
        internal bool HasPreparedInstruction => _executor.HasPreparedInstruction;

        internal PreparedArithmeticOperation? CaptureArithmeticOperand(in PreparedInstruction instruction) =>
            _executor.CaptureArithmeticOperand(in instruction);
        internal bool RequiresArithmeticResult(in PreparedInstruction instruction) =>
            _executor.RequiresArithmeticResult(in instruction);
        internal byte GetPreparedImmediateOperand(in PreparedInstruction instruction) =>
            _executor.GetPreparedImmediateOperand(in instruction);
        internal void SupplyArithmeticResult(in PreparedInstruction instruction,
            NormalizedArithmeticResult? result, ProcessorException? failure) =>
            _executor.SupplyArithmeticResult(in instruction, result, failure);
        internal bool IsPreparedInstructionActive(in PreparedInstruction instruction) =>
            _executor.IsPreparedInstructionActive(in instruction);
        internal bool CompleteInstruction(in PreparedInstruction instruction) =>
            _executor.CompleteInstruction(in instruction);
        internal void CancelInstruction(in PreparedInstruction instruction) =>
            _executor.CancelInstruction(in instruction);

        #region Typed trace bridge

        internal void CanonPre(ulong word, uint rk, DecodedInstruction instruction)
        {
            InstructionAccounting?.Begin(instruction.Opcode);
            if (_traceController.IsEnabled)
                _traceController.Begin(new Word48(word), rk, instruction);
        }

        internal void CanonPost(uint k, bool right)
        {
            InstructionAccounting?.Complete();
            if (_traceController.HasPending)
                _traceController.Complete();
        }

        internal void CancelInstructionTrace()
        {
            InstructionAccounting?.CancelPending();
            _traceController.CancelPending();
        }

        #endregion
    }
}
