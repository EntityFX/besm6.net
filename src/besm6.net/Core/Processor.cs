using System;
using System.IO;
using System.Text;

namespace Besm6.Core
{
    /// <summary>
    /// Точный порт процессора БЭСМ-6 из C++ референса (dubna/processor.cpp
    /// и dubna/arithmetic.cpp). Полный набор инструкций.
    /// </summary>
    public class Processor
    {
        // Регистр режима АЛУ (RAU).
        private const uint RAU_LOG = (uint)RauFlags.Log;
        private const uint RAU_MULT = (uint)RauFlags.Mult;
        private const uint RAU_ADD = (uint)RauFlags.Add;
        private const uint RAU_MODE = (uint)RauFlags.Mode;

        // Биты (нумерация БЭСМ-6: 40-й бит = битовый индекс 39 и т.д.)
        private const ulong BIT41 = Besm6Constants.BIT41;
        private const ulong BIT48 = Besm6Constants.BIT48;
        private const ulong BIT49 = Besm6Constants.BIT49;
        private const ulong BITS40 = Besm6Constants.BITS40;
        private const ulong BITS41 = Besm6Constants.BITS41;
        private const ulong BITS48 = Besm6Constants.BITS48;

        // Внутреннее состояние процессора (CoreState).
        internal uint _pc;              // счётчик команд (Program Counter)
        internal Word48 _acc;             // сумматор (ACC)
        internal Word48 _rmr;             // регистр младших разрядов (RMR)
        internal readonly uint[] _m = new uint[16]; // индекс-регистры M[0..15]
        internal uint _mod;             // регистр модификации MOD
        internal uint _rau;             // режим АЛУ
        internal int _interceptCount;   // перехват overflow/div-by-zero (E75 при addr==020)
        internal uint _interceptAddr = 16; // адрес перехвата; по умолчанию 020 (oct) = 16 (dec), как C++ intercept_addr{020}
        internal bool _rightInstrFlag;  // выполнять правую половину слова
        internal bool _applyModReg;     // модифицировать адрес через MOD
        internal int _corrStack;        // C++ int corr_stack{} (processor.h:93) — поправка M[017] при прерывании инструкции

        internal uint _rk;              // регистр команд
        internal uint _aex;             // исполнительный адрес

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
        /// Хук трассировки инструкции — точный аналог C++ <c>Processor::print_instruction()</c>
        /// (ref/trace.cpp:240). Вызывается в НАЧАЛЕ инструкции: после fetch RK и decode
        /// (reg/addr/opcode), НО до advance PC и до исполнения (ref/processor.cpp:151).
        /// Аргументы: (pc, rightFlag, rk, opcode). null = выключен.
        /// </summary>
        public Action<uint, bool, uint, uint>? TraceInstruction { get; set; }

        // Поля текущей инструкции экстракода для трассировки в формате C++ (см. ref/trace.cpp print_instruction).
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
            _pc = 1;
            _acc = Word48.FromInt48(0);
            _rmr = Word48.FromInt48(0);
            for (int i = 0; i < 16; i++) _m[i] = 0;
            _mod = 0;
            _rau = 0;
            _interceptCount = 0;
            _rightInstrFlag = false;
            _applyModReg = false;
            _corrStack = 0;
        }

        #region Доступ к регистрам (для тестов)

        public uint PC { get => _pc; set => _pc = value; }
        public Word48 Acc { get => _acc; set => _acc = value; }
        public Word48 Rmr => _rmr;
        public uint Rau { get => _rau; set => _rau = value & 0x3F; }
        public bool OnRightInstruction => _rightInstrFlag;

        /// <summary>Флаг применения MOD к адресам (C++ core.apply_mod_reg) — для трассировки регистров.</summary>
        public bool ApplyModReg => _applyModReg;

        /// <summary>Человекочитаемый режим АЛУ (для отладчика/панели).</summary>
        public string AluMode => IsLogical() ? "LOG" : (IsMultiplicative() ? "MUL" : "ADD");
        /// <summary>Сигнальный флаг правого полу-слова.</summary>
        public bool RightInstruction => _rightInstrFlag;
        /// <summary>Регистр модификации MOD (для отладчика/панели).</summary>
        public long Mod => _mod;
        /// <summary>
        /// Счётчик перехвата (intercept_count): 0 — перехват отключён,
        /// 1 — перехватить следующую ошибку арифметики (overflow/div-by-zero).
        /// Ставится E75 при записи по addr == 020 (C++ e75).
        /// </summary>
        public int InterceptCount { get => _interceptCount; set => _interceptCount = value; }
        /// <summary>Потребить перехват (после срабатывания ошибки).</summary>
        public void ConsumeIntercept() => _interceptCount = 0;
        /// <summary>Адрес перехвата (по умолчанию 020 oct = 16 dec, как C++ intercept_addr).</summary>
        public uint InterceptAddr { get => _interceptAddr; set => _interceptAddr = value; }

        /// <summary>
        /// Перехват арифметической ошибки (overflow / div-zero). Точный порт
        /// C++ Processor::intercept (dubna/processor.cpp:68-85):
        /// если перехват вооружён (InterceptCount>0) и сообщение — "Arithmetic overflow"
        /// или "Division by zero", то InterceptCount--, PC=InterceptAddr,
        /// right_instr_flag=false, apply_mod_reg=false, MOD=0, вернуть true.
        /// Иначе вернуть false (перехват отключён — ошибка не перехватывается).
        /// </summary>
        public bool Intercept(string message)
        {
            if (_interceptCount > 0 &&
                (message == "Arithmetic overflow" || message == "Division by zero"))
            {
                _interceptCount--;
                _pc = _interceptAddr & 0x7FFF;
                _rightInstrFlag = false;
                _applyModReg = false;
                _mod = 0;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Корректировка стека при перехвате. Точный порт C++ Processor::stack_correction
        /// (dubna/processor.cpp:127-131): core.M[017] += corr_stack; corr_stack = 0.
        /// corr_stack выставляется инструкциями, предварительно изменившими M[017]
        /// (сл/вч/.../стx: +1, счм: -1, уим/мод(стек): +1) и сбрасывается в начале
        /// каждой инструкции (C++ processor.cpp:184). Вызывается при арифметическом
        /// исключении ПЕРЕД Intercept — как в C++ machine.cpp:131-134 — чтобы откатить
        /// недоисполненное изменение стека.
        /// </summary>
        public void StackCorrection()
        {
            // C++: core.M[017] += corr_stack; corr_stack = 0;
            // Без ADDR-маски — ровно как в C++ (беззнаковое сложение по модулю слова).
            _m[15] = (uint)(_m[15] + _corrStack);
            _corrStack = 0;
        }

        public void SetPc(uint val) => _pc = val;
        public void SetM(int index, uint val) => _m[index & 0xF] = val;
        public void SetRau(ulong val) => _rau = (uint)(val & 0x3F);
        public void SetAcc(ulong val) => _acc = Word48.FromInt48(val & BITS48);
        public void SetRmr(ulong val) => _rmr = Word48.FromInt48(val & BITS48);

        public uint GetPc() => _pc;
        public uint GetM(int index) => _m[index & 0xF];
        public uint GetRau() => _rau;
        public Word48 GetAcc() => _acc;
        public Word48 GetRmr() => _rmr;

        #endregion

        #region Арифметика АЛУ (делегирование в Alu)

        public void ArithAdd(Word48 val, bool negateAcc, bool negateVal) => _alu.Add(val, negateAcc, negateVal);
        public void ArithAddExponent(int val) => _alu.AddExponent(val);
        public void ArithChangeSign(bool negateAcc) => _alu.ChangeSign(negateAcc);
        public void ArithMultiply(Word48 val) => _alu.Multiply(val);
        public void ArithDivide(Word48 val) => _alu.Divide(val);
        public void ArithShift(int nbits) => _alu.Shift(nbits);

        #endregion

        #region Вспомогательные операции (порт besm6_arch.cpp)

        private static uint Addr(uint x) => Besm6Constants.Addr(x);

        private static ulong OnBit(int n) => Besm6Constants.OnBit(n);

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

        internal bool IsAdditive() => (_rau & RAU_ADD) != 0;
        internal bool IsMultiplicative() => (_rau & (RAU_ADD | RAU_MULT)) == RAU_MULT;
        internal bool IsLogical() => (_rau & RAU_MODE) == RAU_LOG;

        internal void SetAdditive() { _rau = (_rau & ~RAU_MODE) | RAU_ADD; }
        internal void SetMultiplicative() { _rau = (_rau & ~RAU_MODE) | RAU_MULT; }
        internal void SetLogical() { _rau = (_rau & ~RAU_MODE) | RAU_LOG; }

        #endregion

        #region Память (с семантикой адреса 0, как в C++ Machine)

        internal ulong MemFetch(ulong addr)
        {
            addr &= 0x7FFF;
            if (addr == 0)
                return 0;
            return _memory.Read((uint)addr).Value;
        }

        internal ulong MemLoad(uint addr)
        {
            addr &= 0x7FFF;
            if (addr == 0)
                return 0;
            return _memory.Read(addr).Value;
        }

        internal void MemStore(uint addr, ulong val)
        {
            addr &= 0x7FFF;
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

        #region Canonical TSV trace (дифференциальное сравнение с C++ dubna)

        /// <summary>
        /// Канонический машинно-сравнимый трасс (TSV). Включается env-переменной
        /// BESM6_CANON_TRACE=путь. Одна строка = одна реально выполненная инструкция:
        /// PRE-снимок состояния (ДО advance PC/half и ДО исполнения) + POST-снимок.
        /// Схема колонок идентична instrumented C++ (ref/processor.cpp canon_pre/canon_post).
        /// half = исполняемая половина (L: старшие 24 бита слова, R: младшие).
        /// Все адреса — unsigned decimal; ACC/RMR/raw48/rk24 — hex.
        /// </summary>
        private StreamWriter? _canonTrace;
        private bool _canonOn;
        private bool _canonChecked;
        private ulong _canonSeq;

        private void CanonCheck()
        {
            if (_canonChecked) return;
            _canonChecked = true;
            string? path = Environment.GetEnvironmentVariable("BESM6_CANON_TRACE");
            if (string.IsNullOrEmpty(path)) return;
            var w = new StreamWriter(path, false, new UTF8Encoding(false));
            w.WriteLine("seq\tpc\thalf\traw48\trk24\topcode\treg\taddr");
            w.WriteLine("acc_b\trmr_b\trau_b\tmod_b\tamod_b\taex_b\ticnt_b\tiadr_b");
            for (int i = 0; i < 16; i++) w.Write("m" + i + "_b\t");
            w.WriteLine();
            w.WriteLine("acc_a\trmr_a\trau_a\tmod_a\tamod_a\taex_a\ticnt_a\tiadr_a\tpc_a\thalf_a");
            for (int i = 0; i < 16; i++) w.Write("m" + i + "_a\t");
            w.WriteLine();
            _canonTrace = w;
            _canonOn = true;
        }

        internal bool CanonOn
        {
            get { CanonCheck(); return _canonOn; }
        }

        internal void CanonPre(uint pc, bool right, ulong word, uint rk, uint opcode, int reg, uint addr)
        {
            CanonCheck();
            if (!_canonOn) return;
            ulong seq = _canonSeq++;
            var sb = new StringBuilder(256);
            sb.Append(seq).Append('\t')
              .Append(pc).Append('\t')
              .Append(right ? 'R' : 'L').Append('\t')
              .Append(word.ToString("X12")).Append('\t')
              .Append(rk.ToString("X6")).Append('\t')
              .Append(opcode).Append('\t')
              .Append(reg).Append('\t')
              .Append(addr).Append('\n')
              .Append(_acc.Value.ToString("X12")).Append('\t')
              .Append(_rmr.Value.ToString("X12")).Append('\t')
              .Append(_rau).Append('\t')
              .Append(_mod).Append('\t')
              .Append(_applyModReg ? 1 : 0).Append('\t')
              .Append(_aex).Append('\t')
              .Append(_interceptCount).Append('\t')
              .Append(_interceptAddr).Append('\n');
            for (int i = 0; i < 16; i++) sb.Append(_m[i]).Append(i == 15 ? '\n' : '\t');
            _canonTrace!.Write(sb.ToString());
        }

        internal void CanonPost(uint pc, bool right)
        {
            if (!_canonOn) return;
            var sb = new StringBuilder(256);
            sb.Append(_acc.Value.ToString("X12")).Append('\t')
              .Append(_rmr.Value.ToString("X12")).Append('\t')
              .Append(_rau).Append('\t')
              .Append(_mod).Append('\t')
              .Append(_applyModReg ? 1 : 0).Append('\t')
              .Append(_aex).Append('\t')
              .Append(_interceptCount).Append('\t')
              .Append(_interceptAddr).Append('\t')
              .Append(pc).Append('\t')
              .Append(right ? 'R' : 'L').Append('\n');
            for (int i = 0; i < 16; i++) sb.Append(_m[i]).Append(i == 15 ? '\n' : '\t');
            _canonTrace!.Write(sb.ToString());
        }

        internal void CanonFlush()
        {
            _canonTrace?.Flush();
        }

        #endregion
    }

    public class ProcessorException : Exception
    {
        public ProcessorException(string message) : base(message) { }
    }
}