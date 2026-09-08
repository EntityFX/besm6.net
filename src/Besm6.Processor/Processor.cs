using System;
using System.IO;
using System.Text;

namespace Besm6.Core
{
    /// <summary>
    /// и dubna/arithmetic.cpp). Полный набор инструкций.
    /// </summary>
    public class Processor
    {
        // Регистр режима арифметического устройства R (РАУ).
        private const uint R_LOG = (uint)RFlags.Log;
        private const uint R_MULT = (uint)RFlags.Mult;
        private const uint R_ADD = (uint)RFlags.Add;
        private const uint R_MODE = (uint)RFlags.Mode;

        // Биты (нумерация БЭСМ-6: 40-й бит = битовый индекс 39 и т.д.)
        private const ulong BIT41 = ArchitectureConstants.BIT41;
        private const ulong BIT48 = ArchitectureConstants.BIT48;
        private const ulong BIT49 = ArchitectureConstants.BIT49;
        private const ulong BITS40 = ArchitectureConstants.BITS40;
        private const ulong BITS41 = ArchitectureConstants.BITS41;
        private const ulong BITS48 = ArchitectureConstants.BITS48;

        // Внутреннее состояние процессора (CoreState).
        internal uint _k;               // счётчик команд K
        internal Word48 _a;             // аккумулятор A
        internal Word48 _y;             // регистр младших разрядов Y
        internal readonly uint[] _m = new uint[16]; // индекс-регистры M[0..15]
        internal uint _c;               // регистр модификации адреса C
        internal uint _r;               // регистр режима арифметического устройства R
        internal int _interceptCount;   // перехват overflow/div-by-zero (E75 при addr==020)
        internal uint _interceptAddr = 16;
        internal bool _rightInstrFlag;  // выполнять правую половину слова
        internal bool _applyC;          // применить C к адресу следующей инструкции
        internal int _corrStack;

        internal uint _rk;              // регистр команд
        internal uint _aex;             // исполнительный адрес

        private bool _debugFetchArmed;
        private uint _debugFetchAddr;
        private uint _debugFetchCont;
        private bool _debugFetchPrintInfo;
        private bool _debugMemoryArmed;
        private uint _debugMemoryAddr;
        private uint _debugMemoryCont;
        private bool _debugMemoryPrintInfo;
        private uint _debugMemoryMode;
        private bool _debugWatchSuppressed;
        private uint _debugPrevAbort;

        private readonly IMemory _memory;
        internal readonly Alu _alu;
        internal readonly InstructionExecutor _executor;

        /// <summary>
        /// Необязательный обработчик экстракодов (Э50..Э77, Э20, Э21).
        /// Позволяет подсистеме загрузчика (Besm6.Loader) перехватывать экстракоды
        /// вместо выброса исключения. Вызывается с кодом экстракода и исполнительным
        /// адресом. Должен вернуть true, если экстракод обработан (исполнение
        /// продолжается), либо false, чтобы поведение осталось прежним (исключение).
        /// Если обработчик не назначен — поведение не меняется.
        /// </summary>
        public Func<int, uint, bool>? ExtracodeHandler { get; set; }

        /// <summary>
        /// (ref/trace.cpp:240). Вызывается в НАЧАЛЕ инструкции: после fetch RK и decode
        /// (reg/addr/opcode), НО до advance K и до исполнения (ref/processor.cpp:151).
        /// Аргументы: (k, rightFlag, rk, opcode). null = выключен.
        /// </summary>
        public Action<uint, bool, uint, uint>? TraceInstruction { get; set; }

        // Заполняются InstructionExecutor.ExtracodeDispatch перед вызовом ExtracodeHandler.
        public int ExtracodeReg { get; set; }
        public uint ExtracodeRawAddr { get; set; }
        public bool ExtracodeRightFlag { get; set; }

        public Processor(IMemory memory)
        {
            _memory = memory;
            _alu = new Alu(this);
            _executor = new InstructionExecutor(this);
            Reset();
        }

        public void Reset()
        {
            _k = 1;
            _a = Word48.FromInt48(0);
            _y = Word48.FromInt48(0);
            for (int i = 0; i < 16; i++) _m[i] = 0;
            _c = 0;
            _r = 0;
            _interceptCount = 0;
            _rightInstrFlag = false;
            _applyC = false;
            _corrStack = 0;
            _debugFetchArmed = false;
            _debugMemoryArmed = false;
            _debugWatchSuppressed = false;
            _debugPrevAbort = 0;
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
        {
            xfer &= 0x7FFF;
            watch &= 0x7FFF;
            cont &= 0x7FFF;
            if (xfer == 0)
                xfer = _debugPrevAbort != 0 ? _debugPrevAbort : cont;

            switch (mode)
            {
                case 0:
                    _debugFetchArmed = true;
                    _debugFetchAddr = watch;
                    _debugFetchCont = cont;
                    _debugFetchPrintInfo = printInfo;
                    break;
                case 1:
                case 2:
                    _debugMemoryArmed = true;
                    _debugMemoryAddr = watch;
                    _debugMemoryCont = cont;
                    _debugMemoryPrintInfo = printInfo;
                    _debugMemoryMode = mode;
                    break;
                default:
                    throw new ProcessorException("Bad debug watchpoint mode");
            }

            _k = xfer;
            _rightInstrFlag = false;
        }

        internal bool DebugCheckFetch(uint addr, uint opcode)
        {
            if (_debugWatchSuppressed || !_debugFetchArmed || _debugFetchAddr != (addr & 0x7FFF))
                return false;

            uint cont = _debugFetchCont;
            bool printInfo = _debugFetchPrintInfo;
            _debugFetchArmed = false;
            DebugFire(cont, printInfo, opcode);
            return true;
        }

        private bool DebugCheckMemory(uint addr, uint mode)
        {
            if (_debugWatchSuppressed || !_debugMemoryArmed ||
                _debugMemoryMode != mode || _debugMemoryAddr != (addr & 0x7FFF))
                return false;

            uint cont = _debugMemoryCont;
            bool printInfo = _debugMemoryPrintInfo;
            _debugMemoryArmed = false;
            DebugFire(cont, printInfo, 0);
            return true;
        }

        private void DebugFire(uint cont, bool printInfo, uint opcode)
        {
            _debugWatchSuppressed = true;
            try
            {
                if (printInfo)
                    TraceInstruction?.Invoke(_k, _rightInstrFlag, _rk, opcode);

                _debugPrevAbort = cont;
                _k = cont & 0x7FFF;
                _rightInstrFlag = false;
                _applyC = false;
                _c = 0;
            }
            finally
            {
                _debugWatchSuppressed = false;
            }
        }

        #endregion

        #region Арифметика АЛУ (делегирование в Alu)

        public void ArithAdd(Word48 val, bool negateA, bool negateVal) => _alu.Add(val, negateA, negateVal);
        public void ArithAddExponent(int val) => _alu.AddExponent(val);
        public void ArithChangeSign(bool negateA) => _alu.ChangeSign(negateA);
        public void ArithMultiply(Word48 val) => _alu.Multiply(val);
        public void ArithDivide(Word48 val) => _alu.Divide(val);
        public void ArithShift(int nbits) => _alu.Shift(nbits);

        #endregion

        #region Вспомогательные операции (порт besm6_arch.cpp)

        private static uint Addr(uint x) => ArchitectureConstants.NormalizeAddress(x);

        private static ulong OnBit(int n) => ArchitectureConstants.OnBit(n);

        internal static int Besm6HighestBit(ulong val)
        {
            int n = 32, cnt = 0;
            do
            {
                ulong tmp = val;
                if ((tmp >>= n) != 0)
                {
                    cnt += n;
                    val = tmp;
                }
            } while ((n >>= 1) != 0);
            return 48 - cnt;
        }

        internal static int Besm6CountOnes(ulong word)
        {
            int c = 0;
            while (word != 0)
            {
                word &= word - 1;
                c++;
            }
            return c;
        }

        internal static ulong Besm6Pack(ulong val, ulong mask)
        {
            ulong result = 0;
            while (mask != 0)
            {
                if ((mask & 1) != 0)
                {
                    result >>= 1;
                    if ((val & 1) != 0)
                        result |= BIT48;
                }
                mask >>= 1;
                val >>= 1;
            }
            return result & BITS48;
        }

        internal static ulong Besm6Unpack(ulong val, ulong mask)
        {
            ulong result = 0;
            for (int i = 0; i < 48; i++)
            {
                result <<= 1;
                if ((mask & BIT48) != 0)
                {
                    if ((val & BIT48) != 0)
                        result |= 1;
                    val <<= 1;
                }
                mask <<= 1;
            }
            return result & BITS48;
        }

        #endregion

        #region Режим АЛУ

        internal bool IsAdditive() => (_r & R_ADD) != 0;
        internal bool IsMultiplicative() => (_r & (R_ADD | R_MULT)) == R_MULT;
        internal bool IsLogical() => (_r & R_MODE) == R_LOG;

        internal void SetAdditive() { _r = (_r & ~R_MODE) | R_ADD; }
        internal void SetMultiplicative() { _r = (_r & ~R_MODE) | R_MULT; }
        internal void SetLogical() { _r = (_r & ~R_MODE) | R_LOG; }

        #endregion

        #region Память

        internal ulong MemFetch(ulong addr)
        {
            addr &= 0x7FFF;
            if (addr == 0)
                throw new ProcessorException("Jump to zero");
            return _memory.Read((uint)addr).Value;
        }

        internal ulong MemLoad(uint addr)
        {
            addr &= 0x7FFF;
            if (DebugCheckMemory(addr, 2))
                throw new DebugWatchAbortException();
            if (addr == 0)
                return 0;
            return _memory.Read(addr).Value;
        }

        internal void MemStore(uint addr, ulong val)
        {
            addr &= 0x7FFF;
            if (DebugCheckMemory(addr, 1))
                throw new DebugWatchAbortException();
            if (addr == 0)
                return;
            _memory.Write(addr, new Word48(val));
        }

        #endregion

        /// <summary>
        /// Выполняет одну инструкцию (левую или правую половину слова).
        /// Возвращает true, когда процессор остановлен (инструкция СТОП).
        /// </summary>
        public bool Step() => _executor.Execute();

        #region Canonical TSV trace

        /// <summary>
        /// Канонический машинно-сравнимый трасс (TSV). Включается env-переменной
        /// BESM6_CANON_TRACE=путь. Одна строка = одна реально выполненная инструкция:
        /// PRE-снимок состояния (ДО advance K/half и ДО исполнения) + POST-снимок.
        /// half = исполняемая половина (L: старшие 24 бита слова, R: младшие).
        /// Все адреса — unsigned decimal; A/Y/raw48/rk24 — hex.
        /// </summary>
        private StreamWriter? _canonTrace;
        private bool _canonOn;
        private bool _canonChecked;
        private ulong _canonSeq;
        private ulong _canonLimit = ulong.MaxValue;
        private StringBuilder? _canonPending;

        private void CanonCheck()
        {
            if (_canonChecked) return;
            _canonChecked = true;
            string? path = Environment.GetEnvironmentVariable("BESM6_CANON_TRACE");
            if (string.IsNullOrEmpty(path)) return;
            string? limitText = Environment.GetEnvironmentVariable("BESM6_CANON_TRACE_LIMIT");
            if (!string.IsNullOrWhiteSpace(limitText) && ulong.TryParse(limitText, out ulong limit))
                _canonLimit = limit;
            var w = new StreamWriter(path, false, new UTF8Encoding(false));
            var header = new StringBuilder(512);
            header.Append("seq\tpc\thalf\traw48\trk24\topcode\treg\taddr")
                  .Append("\ta_b\ty_b\tr_b\tc_b\tapply_c_b\taex_b\ticnt_b\tiadr_b");
            for (int i = 0; i < 16; i++) header.Append("\tm").Append(i).Append("_b");
            header.Append("\ta_a\ty_a\tr_a\tc_a\tapply_c_a\taex_a\ticnt_a\tiadr_a\tpc_a\thalf_a");
            for (int i = 0; i < 16; i++) header.Append("\tm").Append(i).Append("_a");
            w.WriteLine(header.ToString());
            _canonTrace = w;
            _canonOn = true;
        }

        internal bool CanonOn
        {
            get { CanonCheck(); return _canonOn; }
        }

        internal void CanonPre(uint k, bool right, ulong word, uint rk, uint opcode, int reg, uint addr)
        {
            CanonCheck();
            if (!_canonOn) return;
            if (_canonSeq >= _canonLimit)
            {
                _canonPending = null;
                return;
            }
            ulong seq = _canonSeq++;
            var sb = new StringBuilder(512);
            sb.Append(seq)
              .Append('\t').Append(k)
              .Append('\t').Append(right ? 'R' : 'L')
              .Append('\t').Append(word.ToString("X12"))
              .Append('\t').Append(rk.ToString("X6"))
              .Append('\t').Append(opcode)
              .Append('\t').Append(reg)
              .Append('\t').Append(addr)
              .Append('\t').Append(_a.Value.ToString("X12"))
              .Append('\t').Append(_y.Value.ToString("X12"))
              .Append('\t').Append(_r)
              .Append('\t').Append(_c)
              .Append('\t').Append(_applyC ? 1 : 0)
              .Append('\t').Append(_aex)
              .Append('\t').Append(_interceptCount)
              .Append('\t').Append(_interceptAddr);
            for (int i = 0; i < 16; i++) sb.Append('\t').Append(_m[i]);
            _canonPending = sb;
        }

        internal void CanonPost(uint k, bool right)
        {
            if (!_canonOn) return;
            if (_canonPending == null) return;
            var sb = _canonPending;
            sb.Append('\t').Append(_a.Value.ToString("X12"))
              .Append('\t').Append(_y.Value.ToString("X12"))
              .Append('\t').Append(_r)
              .Append('\t').Append(_c)
              .Append('\t').Append(_applyC ? 1 : 0)
              .Append('\t').Append(_aex)
              .Append('\t').Append(_interceptCount)
              .Append('\t').Append(_interceptAddr)
              .Append('\t').Append(k)
              .Append('\t').Append(right ? 'R' : 'L');
            for (int i = 0; i < 16; i++) sb.Append('\t').Append(_m[i]);
            _canonTrace!.WriteLine(sb.ToString());
            _canonPending = null;
        }

        internal void CanonFlush()
        {
            _canonTrace?.Flush();
        }

        #endregion
    }
}
