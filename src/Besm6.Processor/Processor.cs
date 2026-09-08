using System;

namespace Besm6.Core
{
    /// <summary>
    /// и dubna/arithmetic.cpp). Полный набор инструкций.
    /// </summary>
    public class Processor
    {
        // Биты (нумерация БЭСМ-6: 40-й бит = битовый индекс 39 и т.д.)
        private const ulong BIT41 = ArchitectureConstants.BIT41;
        private const ulong BIT48 = ArchitectureConstants.BIT48;
        private const ulong BIT49 = 1UL << 48;
        private const ulong BITS40 = ArchitectureConstants.BITS40;
        private const ulong BITS41 = ArchitectureConstants.BITS41;
        private const ulong BITS48 = ArchitectureConstants.BITS48;

        private readonly ProcessorState _state;
        private readonly ProcessorTraceController _traceController;

        // Временные ref-мосты сохраняют внутренний API на время разбиения executor/ALU.
        // Единственным владельцем значений остаётся ProcessorState.
        internal ref uint _k => ref _state.K;
        internal Word48 _a { get => _state.A; set => _state.A = value; }
        internal Word48 _y { get => _state.Y; set => _state.Y = value; }
        internal uint[] _m => _state.M;
        internal ref uint _c => ref _state.C;
        internal ref uint _r => ref _state.R;
        internal ref int _interceptCount => ref _state.InterceptCount;
        internal ref uint _interceptAddr => ref _state.InterceptAddress;
        internal ref bool _rightInstrFlag => ref _state.IsRightHalf;
        internal ref bool _applyC => ref _state.ApplyC;
        internal ref int _corrStack => ref _state.StackCorrection;
        internal ref uint _rk => ref _state.RawInstruction;
        internal ref uint _aex => ref _state.EffectiveAddress;
        internal ProcessorState State => _state;

        private readonly ProcessorDebugWatch _debugWatch;
        private readonly ProcessorMemoryAccess _memoryAccess;
        internal readonly Alu _alu;
        internal readonly InstructionExecutor _executor;

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

        public Processor(IMemory memory)
        {
            _state = new ProcessorState();
            _traceController = new ProcessorTraceController(_state);
            _debugWatch = new ProcessorDebugWatch(this, _state);
            _memoryAccess = new ProcessorMemoryAccess(_debugWatch, memory);
            _alu = new Alu(_state);
            _executor = new InstructionExecutor(this, _state, _memoryAccess, _alu);
            Reset();
        }

        public void Reset()
        {
            _state.Reset();
            _traceController.Reset();
            _debugWatch.Reset();
        }

        #region Доступ к регистрам (для тестов)

        /// <summary>K — счётчик команд.</summary>
        public uint K { get => _k; set => _k = value; }
        /// <summary>A — аккумулятор.</summary>
        public Word48 A { get => _a; set => _a = value; }
        /// <summary>Y — регистр младших разрядов.</summary>
        public Word48 Y => _y;
        /// <summary>R — регистр режима арифметического устройства.</summary>
        public uint R { get => _r; set => _r = value & 0x3F; }
        /// <summary>M — индексные регистры.</summary>
        public ReadOnlySpan<uint> M => _m;
        /// <summary>C — регистр модификации адреса.</summary>
        public uint C => _c;
        public bool OnRightInstruction => _rightInstrFlag;

        /// <summary>Признак применения регистра C к следующей инструкции.</summary>
        public bool ApplyC => _applyC;

        /// <summary>Человекочитаемый режим регистра R.</summary>
        public string RMode => IsLogical() ? "LOG" : (IsMultiplicative() ? "MUL" : "ADD");
        /// <summary>Сигнальный флаг правого полу-слова.</summary>
        public bool RightInstruction => _rightInstrFlag;
        /// <summary>
        /// Счётчик перехвата (intercept_count): 0 — перехват отключён,
        /// 1 — перехватить следующую ошибку арифметики (overflow/div-by-zero).
        /// </summary>
        public int InterceptCount { get => _interceptCount; set => _interceptCount = value; }
        /// <summary>Потребить перехват (после срабатывания ошибки).</summary>
        public void ConsumeIntercept() => _interceptCount = 0;
        public uint InterceptAddr { get => _interceptAddr; set => _interceptAddr = value; }

        /// <summary>
        /// Перехват арифметической ошибки (overflow / div-zero). Точный порт
        /// если перехват вооружён (InterceptCount>0) и сообщение — "Arithmetic overflow"
        /// или "Division by zero", то InterceptCount--, K=InterceptAddr,
        /// right_instr_flag=false, apply_c_reg=false, C=0, вернуть true.
        /// Иначе вернуть false (перехват отключён — ошибка не перехватывается).
        /// </summary>
        public bool Intercept(string message)
        {
            if (_interceptCount > 0 &&
                (message == "Arithmetic overflow" || message == "Division by zero"))
            {
                _interceptCount--;
                _k = _interceptAddr & 0x7FFF;
                _rightInstrFlag = false;
                _applyC = false;
                _c = 0;
                return true;
            }
            return false;
        }

        /// <summary>
        /// (dubna/processor.cpp:127-131): core.M[017] += corr_stack; corr_stack = 0.
        /// corr_stack выставляется инструкциями, предварительно изменившими M[017]
        /// (сл/вч/.../стx: +1, счм: -1, уим/мод(стек): +1) и сбрасывается в начале
        /// недоисполненное изменение стека.
        /// </summary>
        public void StackCorrection()
        {
            _m[15] = (uint)(_m[15] + _corrStack);
            _corrStack = 0;
        }

        /// <summary>Устанавливает счётчик команд K.</summary>
        public void SetK(uint val) => _k = val;
        /// <summary>Устанавливает индексный регистр M с указанным номером.</summary>
        public void SetM(int index, uint val) => _m[index & 0xF] = val;
        /// <summary>Устанавливает регистр режима R.</summary>
        public void SetR(ulong val) => _r = (uint)(val & 0x3F);
        /// <summary>Устанавливает аккумулятор A.</summary>
        public void SetA(ulong val) => _a = Word48.FromInt48(val & BITS48);
        /// <summary>Устанавливает регистр младших разрядов Y.</summary>
        public void SetY(ulong val) => _y = Word48.FromInt48(val & BITS48);

        /// <summary>Возвращает счётчик команд K.</summary>
        public uint GetK() => _k;
        /// <summary>Возвращает индексный регистр M с указанным номером.</summary>
        public uint GetM(int index) => _m[index & 0xF];
        /// <summary>Возвращает регистр режима R.</summary>
        public uint GetR() => _r;
        /// <summary>Возвращает аккумулятор A.</summary>
        public Word48 GetA() => _a;
        /// <summary>Возвращает регистр младших разрядов Y.</summary>
        public Word48 GetY() => _y;

        internal sealed class DebugWatchAbortException : Exception
        {
        }

        internal void ArmDebugWatch(uint xfer, bool printInfo, uint mode, uint watch, uint cont)
            => _debugWatch.ArmDebugWatch(xfer, printInfo, mode, watch, cont);

        internal bool DebugCheckFetch(uint addr, uint opcode)
            => _debugWatch.DebugCheckFetch(addr, opcode);

        internal bool DebugCheckMemory(uint addr, uint mode)
            => _debugWatch.DebugCheckMemory(addr, mode);

        #endregion

        #region Арифметика АЛУ (делегирование в Alu)

        public void ArithAdd(Word48 val, bool negateA, bool negateVal) => _alu.Add(val, negateA, negateVal);
        public void ArithAddExponent(int val) => _alu.AddExponent(val);
        public void ArithChangeSign(bool negateA) => _alu.ChangeSign(negateA);
        public void ArithMultiply(Word48 val) => _alu.Multiply(val);
        public void ArithDivide(Word48 val) => _alu.Divide(val);
        public void ArithShift(int nbits) => _alu.Shift(nbits);

        #endregion

        #region Вспомогательные операции (делегирование в ProcessorBitOperations)

        private static uint Addr(uint x) => ProcessorBitOperations.Addr(x);

        private static ulong OnBit(int n) => ProcessorBitOperations.OnBit(n);

        internal static int Besm6HighestBit(ulong val) => ProcessorBitOperations.Besm6HighestBit(val);

        internal static int Besm6CountOnes(ulong word) => ProcessorBitOperations.Besm6CountOnes(word);

        internal static ulong Besm6Pack(ulong val, ulong mask) => ProcessorBitOperations.Besm6Pack(val, mask);

        internal static ulong Besm6Unpack(ulong val, ulong mask) => ProcessorBitOperations.Besm6Unpack(val, mask);

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

        internal ulong MemFetch(ulong addr) => _memoryAccess.MemFetch(addr);

        internal ulong MemLoad(uint addr) => _memoryAccess.MemLoad(addr);

        internal void MemStore(uint addr, ulong val) => _memoryAccess.MemStore(addr, val);

        #endregion

        /// <summary>
        /// Выполняет одну инструкцию (левую или правую половину слова).
        /// Возвращает true, когда процессор остановлен (инструкция СТОП).
        /// </summary>
        public bool Step() => _executor.Execute();

        #region Typed trace bridge

        internal void CanonPre(uint k, bool right, ulong word, uint rk, uint opcode, int reg, uint addr)
        {
            _traceController.Begin(
                new Word48(word),
                rk,
                new DecodedInstruction(
                    checked((byte)reg),
                    (Opcode)opcode,
                    checked((ushort)addr),
                    (rk & (1u << 19)) != 0 ? InstructionFormat.Long : InstructionFormat.Short));
        }

        internal void CanonPost(uint k, bool right)
        {
            _traceController.Complete();
        }

        #endregion
    }
}
