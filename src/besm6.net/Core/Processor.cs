using System;

namespace Besm6.Core
{
    /// <summary>
    /// Точный порт процессора БЭСМ-6 из C++ референса (dubna/processor.cpp
    /// и dubna/arithmetic.cpp). Полный набор инструкций.
    /// </summary>
    public class Processor
    {
        // Регистр режима АЛУ (RAU).
        private const long RAU_LOG           = (long)RauFlags.Log;
        private const long RAU_MULT          = (long)RauFlags.Mult;
        private const long RAU_ADD           = (long)RauFlags.Add;
        private const long RAU_MODE          = (long)RauFlags.Mode;

        // Биты (нумерация БЭСМ-6: 40-й бит = битовый индекс 39 и т.д.)
        private const long BIT41  = Besm6Constants.BIT41;
        private const long BIT48  = Besm6Constants.BIT48;
        private const long BIT49  = Besm6Constants.BIT49;
        private const long BITS40 = Besm6Constants.BITS40;
        private const long BITS41 = Besm6Constants.BITS41;
        private const long BITS48 = Besm6Constants.BITS48;

        // Внутреннее состояние процессора (CoreState).
        internal long _pc;              // счётчик команд (Program Counter)
        internal long _acc;             // сумматор (ACC)
        internal long _rmr;             // регистр младших разрядов (RMR)
        internal readonly long[] _m = new long[16]; // индекс-регистры M[0..15]
        internal long _mod;             // регистр модификации MOD
        internal long _rau;             // режим АЛУ
        internal int _interceptCount;   // перехват overflow/div-by-zero (E75 при addr==020)
        internal long _interceptAddr = 16; // адрес перехвата; по умолчанию 020 (oct) = 16 (dec), как C++ intercept_addr{020}
        internal bool _rightInstrFlag;  // выполнять правую половину слова
        internal bool _applyModReg;     // модифицировать адрес через MOD

        internal long _rk;              // регистр команд
        internal long _aex;             // исполнительный адрес

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
        public Func<int, long, bool>? ExtracodeHandler { get; set; }

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
            _acc = 0;
            _rmr = 0;
            for (int i = 0; i < 16; i++) _m[i] = 0;
            _mod = 0;
            _rau = 0;
            _interceptCount = 0;
            _rightInstrFlag = false;
            _applyModReg = false;
        }

        #region Доступ к регистрам (для тестов)

        public long PC { get => _pc; set => _pc = value; }
        public long Acc { get => _acc; set => _acc = value & BITS48; }
        public long Rmr => _rmr;
        public long Rau { get => _rau; set => _rau = value & 0x3F; }
        public bool OnRightInstruction => _rightInstrFlag;

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
        public long InterceptAddr { get => _interceptAddr; set => _interceptAddr = value & 0x7FFF; }

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
        /// Корректировка стека при перехвате. Порт C++ Processor::stack_correction
        /// (dubna/processor.cpp:57-61): core.M[017] += corr_stack; corr_stack = 0.
        /// В C#-порте счётчик corr_stack отсутствует, поэтому это нет-оп.
        /// </summary>
        public void StackCorrection()
        {
            // C++: core.M[017] += corr_stack; corr_stack = 0;
            // C#-порт не реализует corr_stack — метод-заглушка для совместимости с machine.cpp.
        }

        public void SetPc(long val) => _pc = val;
        public void SetM(int index, long val) => _m[index & 0xF] = val;
        public void SetRau(long val) => _rau = val & 0x3F;
        public void SetAcc(long val) => _acc = val & BITS48;
        public void SetRmr(long val) => _rmr = val & BITS48;

        public long GetPc() => _pc;
        public long GetM(int index) => _m[index & 0xF];
        public long GetRau() => _rau;
        public long GetAcc() => _acc;
        public long GetRmr() => _rmr;

        #endregion

        #region Арифметика АЛУ (делегирование в Alu)

        public void ArithAdd(long val, bool negateAcc, bool negateVal) => _alu.Add(val, negateAcc, negateVal);
        public void ArithAddExponent(int val) => _alu.AddExponent(val);
        public void ArithChangeSign(bool negateAcc) => _alu.ChangeSign(negateAcc);
        public void ArithMultiply(long val) => _alu.Multiply(val);
        public void ArithDivide(long val) => _alu.Divide(val);
        public void ArithShift(int nbits) => _alu.Shift(nbits);

        #endregion

        #region Вспомогательные операции (порт besm6_arch.cpp)

        private static long Addr(long x) => Besm6Constants.Addr(x);

        private static long OnBit(int n) => Besm6Constants.OnBit(n);

        internal static int Besm6HighestBit(long val)
        {
            int n = 32, cnt = 0;
            do
            {
                long tmp = val;
                if ((tmp >>= n) != 0)
                {
                    cnt += n;
                    val = tmp;
                }
            } while ((n >>= 1) != 0);
            return 48 - cnt;
        }

        internal static int Besm6CountOnes(long word)
        {
            int c = 0;
            while (word != 0)
            {
                word &= word - 1;
                c++;
            }
            return c;
        }

        internal static long Besm6Pack(long val, long mask)
        {
            long result = 0;
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

        internal static long Besm6Unpack(long val, long mask)
        {
            long result = 0;
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

        internal long MemFetch(long addr)
        {
            addr &= 0x7FFF;
            if (addr == 0)
                return 0;
            return _memory.Read((int)addr).Value;
        }

        internal long MemLoad(long addr)
        {
            addr &= 0x7FFF;
            if (addr == 0)
                return 0;
            return _memory.Read((int)addr).Value;
        }

        internal void MemStore(long addr, long val)
        {
            addr &= 0x7FFF;
            if (addr == 0)
                return;
            _memory.Write((int)addr, new Word48(val));
        }

        #endregion

        /// <summary>
        /// Выполняет одну инструкцию (левую или правую половину слова).
        /// Возвращает true, когда процессор остановлен (инструкция СТОП).
        /// </summary>
        public bool Step() => _executor.Execute();
    }

    public class ProcessorException : Exception
    {
        public ProcessorException(string message) : base(message) { }
    }
}