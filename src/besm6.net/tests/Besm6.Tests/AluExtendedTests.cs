using Besm6.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    /// <summary>
    /// Максимальное покрытие АЛУ (Alu.cs + MantissaExponent.cs).
    /// Покрывает: все 4 режима знака сложения, ветки выравнивания порядка
    /// (diff == 0, 1..40, 41..80, &gt; 80), округление и его отключение (RAU),
    /// RMR (40 бит результата + старшие разряды), переполнение и OVF_DISABLE,
    /// умножение (знаки, денормализованные операнды, RMR), деление (быстрый путь,
    /// невосстанавливающее, деление на ноль), сдвиги (&lt; 48 и &gt;= 48 бит),
    /// изменение порядка (underflow/overflow), смену знака и MantissaExponent напрямую.
    /// (ref/arithmetic.cpp) и сверены с ref/tests/alutest.cpp.
    /// </summary>
    [TestClass]
    public class AluExtendedTests
    {
        private const long B38 = 1L << 38;
        private const long B39 = 1L << 39;
        private const long B40 = 1L << 40;
        private const ulong MASK48 = 0xFFFFFFFFFFFFUL;

        private Processor _cpu;

        [TestInitialize]
        public void Setup()
        {
            _cpu = new Processor(new LinearMemory());
        }

        private sealed class LinearMemory : IMemory
        {
            private readonly Word48[] _words = new Word48[32768];
            public Word48 Read(uint address) => _words[address & 0x7FFF];
            public void Write(uint address, Word48 word) => _words[address & 0x7FFF] = word;
            public int Size => 32768;
        }

        // ─── Помощники ───────────────────────────────────────────────────────────────

        /// <summary>Собирает число БЭСМ-6: 7-битный порядок + знаковая 41-битная мантисса.</summary>
        /// <remarks>
        /// Сдвиг порядка выполняется в <see cref="ulong"/>: сдвиг <c>uint</c> на 41 бит
        /// маскируется по модулю 32 и теряет старшие биты.
        /// </remarks>
        private static Word48 MakeReal(uint exponent, long mantissa)
            => new Word48((((ulong)exponent & 0x7Ful) << 41) | ((ulong)mantissa & 0x1FFFFFFFFFFul));

        /// <summary>Сравнивает ACC и RMR с ожидаемыми словами (бит-в-бит).</summary>
        private void Expect(string op, Word48 expectedAcc, Word48 expectedRmr)
        {
            Assert.AreEqual(expectedAcc, _cpu.GetAcc(),
                $"{op}: ACC = 0o{_cpu.GetAcc().ToOctal()}, ожидалось 0o{expectedAcc.ToOctal()}");
            Assert.AreEqual(expectedRmr, _cpu.GetRmr(),
                $"{op}: RMR = 0o{_cpu.GetRmr().ToOctal()}, ожидалось 0o{expectedRmr.ToOctal()}");
        }

        /// <summary>Проверяет ACC как double с относительной погрешностью (для частного деления).</summary>
        private void ExpectApprox(string op, double expected, double relTol = 1e-11)
        {
            double actual = _cpu.GetAcc().ToDouble();
            Assert.IsTrue(Math.Abs(actual - expected) <= relTol * Math.Max(1.0, Math.Abs(expected)),
                $"{op}: ACC = {actual}, ожидалось ≈ {expected} (слово 0o{_cpu.GetAcc().ToOctal()})");
        }

        /// <summary>Убеждается, что действие бросает <see cref="ProcessorException"/>.</summary>
        private static void ExpectThrows(string op, Action action)
        {
            try
            {
                action();
            }
            catch (ProcessorException)
            {
                return;
            }
            Assert.Fail($"{op}: ожидалось исключение ProcessorException, но оно не выброшено");
        }

        /// <summary>Сравнивает два 64-битных значения без неоднозначных перегрузок AreEqual.</summary>
        private static void ExpectU64(string op, ulong expected, ulong actual)
        {
            Assert.AreEqual(expected, actual,
                $"{op}: ожидалось 0x{expected:X12}, получено 0x{actual:X12}");
        }

        // ─── Сложение/вычитание: все режимы знака ────────────────────────────────────

        [TestMethod]
        public void Add_100_Plus_050_Gives_150()
        {
            _cpu.SetAcc(MakeReal(65, B39).Value); // 1.0
            _cpu.ArithAdd(MakeReal(64, B39), false, false); // + 0.5
            Expect("1.0+0.5", MakeReal(65, 3 * B38), Word48.Zero); // 1.5
        }

        [TestMethod]
        public void Add_100_Minus_050_Gives_050()
        {
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.ArithAdd(MakeReal(64, B39), false, true); // − 0.5
            Expect("1.0−0.5", MakeReal(64, B39), Word48.Zero); // 0.5
        }

        [TestMethod]
        public void Add_NegAcc_Minus_100_Plus_050_Gives_Minus_050()
        {
            // Простое сложение отрицательного ACC с положительным операндом.
            // Результат нормализуется к (порядок 63, мантисса −2^40) = −0.5.
            _cpu.SetAcc(MakeReal(64, -B40).Value); // −1.0
            _cpu.ArithAdd(MakeReal(64, B39), false, false);
            Expect("−1.0+0.5", MakeReal(63, -B40), Word48.Zero); // −0.5
        }

        [TestMethod]
        public void Add_DiffOfAbs_AllSignCombinations()
        {
            // Вычитание модулей: |ACC| − |X| не зависит от исходных знаков.
            _cpu.SetAcc(MakeReal(65, B39).Value); // +1.0
            _cpu.ArithAdd(MakeReal(64, B39), true, true); // +0.5
            Expect("|1.0|−|0.5|", MakeReal(64, B39), Word48.Zero);

            _cpu.SetAcc(MakeReal(64, -B40).Value); // −1.0
            _cpu.ArithAdd(MakeReal(64, B39), true, true);
            Expect("|−1.0|−|0.5|", MakeReal(64, B39), Word48.Zero);

            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.ArithAdd(MakeReal(64, -B39), true, true); // −0.5
            Expect("|1.0|−|−0.5|", MakeReal(64, B39), Word48.Zero);

            _cpu.SetAcc(MakeReal(64, -B40).Value);
            _cpu.ArithAdd(MakeReal(64, -B39), true, true);
            Expect("|−1.0|−|−0.5|", MakeReal(64, B39), Word48.Zero);

            _cpu.SetAcc(MakeReal(64, B39).Value); // 0.5
            _cpu.ArithAdd(MakeReal(65, B39), true, true); // 1.0
            Expect("|0.5|−|1.0|", MakeReal(63, -B40), Word48.Zero); // −0.5
        }

        [TestMethod]
        public void Add_CarryAcrossBit39_150_Plus_150_Gives_300()
        {
            _cpu.SetAcc(MakeReal(65, 3 * B38).Value); // 1.5
            _cpu.ArithAdd(MakeReal(65, 3 * B38), false, false); // + 1.5
            Expect("1.5+1.5", MakeReal(66, 3 * B38), Word48.Zero); // 3.0
        }

        // ─── Ветки выравнивания порядка (diff 1..40 / 41..80 / > 80) + округление ──

        [TestMethod]
        public void Add_RoundUp_Diff40_HalfUlp()
        {
            // 1.0 + 2^-40: потеряны биты -> округление вверх на 1 ULP (2^-39), остаток в RMR.
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.ArithAdd(MakeReal(25, B39), false, false); // + 2^-40
            Expect("1.0+2^-40 (rnd)", MakeReal(65, B39 + 1), new Word48(1UL << 39));
        }

        [TestMethod]
        public void Add_NoRound_Diff40_HalfUlp()
        {
            _cpu.SetRau((ulong)RauFlags.RoundDisable);
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.ArithAdd(MakeReal(25, B39), false, false);
            Expect("1.0+2^-40 (no rnd)", MakeReal(65, B39), new Word48(1UL << 39));
        }

        [TestMethod]
        public void Add_RoundUp_Diff41()
        {
            // diff = 41 -> ветка diff <= 80: mr = mantissa >> 1.
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.ArithAdd(MakeReal(24, B39), false, false); // + 2^-41
            Expect("1.0+2^-41 (rnd)", MakeReal(65, B39 + 1), new Word48(1UL << 38));
        }

        [TestMethod]
        public void Add_NoRound_Diff41()
        {
            _cpu.SetRau((ulong)RauFlags.RoundDisable);
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.ArithAdd(MakeReal(24, B39), false, false);
            Expect("1.0+2^-41 (no rnd)", MakeReal(65, B39), new Word48(1UL << 38));
        }

        [TestMethod]
        public void Add_RoundUp_DiffOver80()
        {
            // 2^62 + 2^-65: diff = 127 -> ветка diff > 80 (положительное слагаемое: mr = 0).
            _cpu.SetAcc(MakeReal(127, B39).Value); // 2^62
            _cpu.ArithAdd(MakeReal(0, B39), false, false); // + 2^-65
            Expect("2^62+2^-65 (rnd)", MakeReal(127, B39 + 1), Word48.Zero);
        }

        [TestMethod]
        public void Add_NoRound_DiffOver80()
        {
            _cpu.SetRau((ulong)RauFlags.RoundDisable);
            _cpu.SetAcc(MakeReal(127, B39).Value);
            _cpu.ArithAdd(MakeReal(0, B39), false, false);
            Expect("2^62+2^-65 (no rnd)", MakeReal(127, B39), Word48.Zero);
        }

        [TestMethod]
        public void Add_Diff0_RmrPreserved()
        {
            // diff == 0 и mr == 0: RMR остаётся равным 0.
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.ArithAdd(MakeReal(65, B39), false, false);
            Expect("1.0+1.0", MakeReal(66, B39), Word48.Zero); // 2.0
        }

        // ─── Переполнение (overflow) ─────────────────────────────────────────────────

        [TestMethod]
        public void Add_Overflow_Throws()
        {
            // 2^62 + 2^62 = 2^64 -> порядок 128 -> переполнение.
            _cpu.SetAcc(MakeReal(127, B39).Value); // 2^62
            ExpectThrows("overflow", () => _cpu.ArithAdd(MakeReal(127, B39), false, false));
        }

        [TestMethod]
        public void Add_Overflow_Disabled()
        {
            _cpu.SetRau((ulong)RauFlags.OvfDisable);
            _cpu.SetAcc(MakeReal(127, B39).Value);
            _cpu.ArithAdd(MakeReal(127, B39), false, false);
            // Порядок 128 маскируется в 0, мантисса сохранена.
            Expect("ovf disabled", MakeReal(0, B39), Word48.Zero);
        }

        [TestMethod]
        public void Add_NegativeMin_NoOverflow()
        {
            // −2^62 + −2^62 = −2^63 — минимальное представимое значение.
            // Отрицательный диапазон БЭСМ-6 шире положительного на один шаг
            // (мантисса может равняться −2^40), поэтому переполнения нет.
            _cpu.SetAcc(MakeReal(127, -B39).Value); // −2^62
            _cpu.ArithAdd(MakeReal(127, -B39), false, false);
            Expect("−2^62+−2^62", MakeReal(127, -B40), Word48.Zero); // −2^63
        }

        // ─── RMR: 40 младших бит результата + старшие разряды ───────────────────────

        [TestMethod]
        public void Add_ZeroResult_KeepsUpperRmr()
        {
            // При результате ноль старшие разряды RMR сохраняются (RMR &= ~BITS40).
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.SetRmr(1UL << 40); // бит 41 (вне 40 бит результата)
            _cpu.ArithAdd(MakeReal(65, B39), false, true); // 1.0 − 1.0
            Expect("1.0−1.0", Word48.Zero, new Word48(1UL << 40));
        }

        [TestMethod]
        public void Add_NonZeroResult_OverwritesRmr()
        {
            // При ненулевом результате RMR перезаписывается (mr & BITS40).
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.SetRmr(1UL << 40);
            _cpu.ArithAdd(MakeReal(65, B39), false, false); // 1.0 + 1.0
            Expect("1.0+1.0", MakeReal(66, B39), Word48.Zero);
        }

        // ─── Умножение ───────────────────────────────────────────────────────────────

        [TestMethod]
        public void Mul_150_Times_150_Gives_225()
        {
            _cpu.SetAcc(MakeReal(65, 3 * B38).Value); // 1.5
            _cpu.ArithMultiply(MakeReal(65, 3 * B38)); // × 1.5
            Expect("1.5×1.5", MakeReal(66, 9L << 36), Word48.Zero); // 2.25
        }

        [TestMethod]
        public void Mul_200_Times_150_Gives_300()
        {
            _cpu.SetAcc(MakeReal(66, B39).Value); // 2.0
            _cpu.ArithMultiply(MakeReal(65, 3 * B38)); // × 1.5
            Expect("2.0×1.5", MakeReal(66, 3 * B38), Word48.Zero); // 3.0
        }

        [TestMethod]
        public void Mul_150_Times_1pUlp_StoresRemainder()
        {
            // 1.5 × (1.0 + 2^-39) = 1.5 + 1.5·2^-39: хвост уходит в RMR.
            // ACC = (порядок 65, мантисса 3·2^38 + 1), RMR = 2^39.
            _cpu.SetAcc(MakeReal(65, 3 * B38).Value);
            _cpu.ArithMultiply(MakeReal(65, B39 + 1));
            Expect("1.5×(1+2^-39)", MakeReal(65, 3 * B38 + 1), new Word48(1UL << 39));
        }

        [TestMethod]
        public void Mul_Minus_150_Times_150_Gives_Minus_225()
        {
            _cpu.SetAcc(MakeReal(65, -(3 * B38)).Value); // −1.5
            _cpu.ArithMultiply(MakeReal(65, 3 * B38)); // × 1.5
            Expect("−1.5×1.5", MakeReal(66, -(9L << 36)), Word48.Zero); // −2.25
        }

        [TestMethod]
        public void Mul_DenormalOperand_IsNormalized()
        {
            // 0.25 = 1/4·2^0 (ненормализованный) × 1.0 = 0.25 = 1/2·2^-1.
            _cpu.SetAcc(MakeReal(64, B38).Value);
            _cpu.ArithMultiply(MakeReal(65, B39));
            Expect("0.25×1.0", MakeReal(63, B39), Word48.Zero);
        }

        [TestMethod]
        public void Mul_ByZero_GivesZero_AndKeepsUpperRmr()
        {
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.SetRmr(1UL << 40);
            _cpu.ArithMultiply(Word48.Zero);
            Expect("×0", Word48.Zero, new Word48(1UL << 40));
        }

        [TestMethod]
        public void Mul_NoNormalize_050_Times_050()
        {
            // RAU=3 (нормализация и округление отключены): результат хранится ненормализованным.
            _cpu.SetRau(3);
            _cpu.SetAcc(MakeReal(64, B39).Value); // 0.5
            _cpu.ArithMultiply(MakeReal(64, B39)); // × 0.5
            Expect("0.5×0.5 (no norm)", MakeReal(64, B38), Word48.Zero); // 1/4·2^0
        }

        [TestMethod]
        public void Mul_NoNormalize_100_Times_100()
        {
            _cpu.SetRau(3);
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.ArithMultiply(MakeReal(65, B39));
            Expect("1.0×1.0 (no norm)", Word48.FromOctal("04104000000000000"), Word48.Zero);
        }

        [TestMethod]
        public void Mul_NoNormalize_100_Times_Minus_100()
        {
            _cpu.SetRau(3);
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.ArithMultiply(MakeReal(64, -B40)); // −1.0
            Expect("1.0×(−1.0) (no norm)", Word48.FromOctal("04070000000000000"), Word48.Zero);
        }

        [TestMethod]
        public void Mul_NoNormalize_Minus_100_Times_100()
        {
            _cpu.SetRau(3);
            _cpu.SetAcc(MakeReal(64, -B40).Value); // −1.0
            _cpu.ArithMultiply(MakeReal(65, B39));
            Expect("−1.0×1.0 (no norm)", Word48.FromOctal("04070000000000000"), Word48.Zero);
        }

        // ─── Деление ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Div_150_By_050_Gives_300()
        {
            // Делитель с мантиссой = BIT40 -> быстрый путь (деление на степень двойки).
            _cpu.SetAcc(MakeReal(65, 3 * B38).Value); // 1.5
            _cpu.ArithDivide(MakeReal(64, B39)); // ÷ 0.5
            Expect("1.5÷0.5", MakeReal(66, 3 * B38), Word48.Zero); // 3.0
        }

        [TestMethod]
        public void Div_200_By_100_Gives_200()
        {
            _cpu.SetAcc(MakeReal(66, B39).Value); // 2.0
            _cpu.ArithDivide(MakeReal(65, B39)); // ÷ 1.0
            Expect("2.0÷1.0", MakeReal(66, B39), Word48.Zero); // 2.0
        }

        [TestMethod]
        public void Div_100_By_300_Gives_OneThird()
        {
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.ArithDivide(MakeReal(66, 3 * B38)); // ÷ 3.0
            ExpectApprox("1.0÷3.0", 1.0 / 3.0);
            Assert.AreEqual(Word48.Zero, _cpu.GetRmr(), "RMR должен быть 0");
        }

        [TestMethod]
        public void Div_200_By_300_Gives_TwoThirds()
        {
            _cpu.SetAcc(MakeReal(66, B39).Value);
            _cpu.ArithDivide(MakeReal(66, 3 * B38));
            ExpectApprox("2.0÷3.0", 2.0 / 3.0);
            Assert.AreEqual(Word48.Zero, _cpu.GetRmr(), "RMR должен быть 0");
        }

        [TestMethod]
        public void Div_150_By_Minus_050_Gives_Minus_300()
        {
            // −0.5 в нормализованном виде: (порядок 63, мантисса −2^40).
            // (Пара (порядок 64, мантисса −2^39) — ненормализованный делитель,
            // АЛУ считает его делением на ноль.)
            _cpu.SetAcc(MakeReal(65, 3 * B38).Value);
            _cpu.ArithDivide(MakeReal(63, -B40)); // ÷ (−0.5)
            ExpectApprox("1.5÷(−0.5)", -3.0);
        }

        [TestMethod]
        public void Div_NoNormalize_100_By_Minus_100()
        {
            _cpu.SetRau(3);
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.ArithDivide(MakeReal(64, -B40));
            Expect("1.0÷(−1.0) (no norm)", Word48.FromOctal("04070000000000000"), Word48.Zero);
        }

        [TestMethod]
        public void Div_NoNormalize_Minus_100_By_Minus_100()
        {
            _cpu.SetRau(3);
            _cpu.SetAcc(MakeReal(64, -B40).Value);
            _cpu.ArithDivide(MakeReal(64, -B40));
            Expect("(−1.0)÷(−1.0) (no norm)", Word48.FromOctal("04050000000000000"), Word48.Zero);
        }

        [TestMethod]
        public void Div_ByZero_Throws()
        {
            _cpu.SetAcc(MakeReal(65, B39).Value);
            ExpectThrows("div by zero", () => _cpu.ArithDivide(Word48.Zero));
        }

        [TestMethod]
        public void Div_ByDenormalZero_Throws()
        {
            // Мантисса = 0 при любом порядке -> деление на ноль.
            _cpu.SetAcc(MakeReal(65, B39).Value);
            ExpectThrows("div by denormal zero", () => _cpu.ArithDivide(MakeReal(64, 0)));
        }

        // ─── Сдвиги (Shift) ─────────────────────────────────────────────────────────

        [TestMethod]
        public void Shift_Right_Less48_Bits()
        {
            const ulong acc = 0x123456789ABCUL;
            _cpu.SetAcc(acc);
            _cpu.ArithShift(5); // вправо на 5 бит: старшие 5 бит уходят в младшие RMR.
            ExpectU64("acc", acc >> 5, _cpu.GetAcc().Value);
            ExpectU64("rmr", (acc << (48 - 5)) & MASK48, _cpu.GetRmr().Value);
        }

        [TestMethod]
        public void Shift_Right_48_Bits()
        {
            const ulong acc = 0x89ABCDEFUL;
            _cpu.SetAcc(acc);
            _cpu.ArithShift(48);
            ExpectU64("acc=0", 0, _cpu.GetAcc().Value);
            ExpectU64("rmr", acc, _cpu.GetRmr().Value);
        }

        [TestMethod]
        public void Shift_Right_MoreThan48_Bits()
        {
            const ulong acc = 0x123456789ABCUL;
            _cpu.SetAcc(acc);
            _cpu.ArithShift(49); // 48 + 1
            ExpectU64("acc=0", 0, _cpu.GetAcc().Value);
            ExpectU64("rmr", acc >> 1, _cpu.GetRmr().Value);
        }

        [TestMethod]
        public void Shift_Left_Less48_Bits()
        {
            const ulong acc = 0x123456789ABCUL;
            _cpu.SetAcc(acc);
            _cpu.ArithShift(-5); // влево на 5 бит
            ExpectU64("acc", (acc << 5) & MASK48, _cpu.GetAcc().Value);
            ExpectU64("rmr", acc >> (48 - 5), _cpu.GetRmr().Value);
        }

        [TestMethod]
        public void Shift_Left_48_Bits()
        {
            const ulong acc = 0x123456789ABCUL;
            _cpu.SetAcc(acc);
            _cpu.ArithShift(-48);
            ExpectU64("acc=0", 0, _cpu.GetAcc().Value);
            ExpectU64("rmr", acc, _cpu.GetRmr().Value);
        }

        [TestMethod]
        public void Shift_Left_MoreThan48_Bits()
        {
            const ulong acc = 0x123456789ABCUL;
            _cpu.SetAcc(acc);
            _cpu.ArithShift(-49); // 48 + 1
            ExpectU64("acc=0", 0, _cpu.GetAcc().Value);
            ExpectU64("rmr", (acc << 1) & MASK48, _cpu.GetRmr().Value);
        }

        [TestMethod]
        public void Shift_Zero_Bits_NoOp()
        {
            const ulong acc = 0x123456789ABCUL;
            _cpu.SetAcc(acc);
            _cpu.ArithShift(0);
            ExpectU64("acc", acc, _cpu.GetAcc().Value);
            ExpectU64("rmr=0", 0, _cpu.GetRmr().Value);
        }

        // ─── Изменение порядка (AddExponent) ────────────────────────────────────────

        [TestMethod]
        public void AddExponent_Increase_One()
        {
            // 1.0 → 2.0 (порядок 65 → 66).
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.ArithAddExponent(1);
            Expect("1.0·2^1", MakeReal(66, B39), Word48.Zero); // 2.0
        }

        [TestMethod]
        public void AddExponent_Decrease_One()
        {
            // 1.0 → 0.5 (порядок 65 → 64).
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.ArithAddExponent(-1);
            Expect("1.0·2^-1", MakeReal(64, B39), Word48.Zero); // 0.5
        }

        [TestMethod]
        public void AddExponent_Underflow_GivesZero()
        {
            // Минимальный порядок 0, шаг −1 переносит знак порядка -> подтекание в 0.
            _cpu.SetAcc(MakeReal(0, B39).Value); // 1/2·2^-64
            _cpu.ArithAddExponent(-1);
            Expect("underflow", Word48.Zero, Word48.Zero);
        }

        [TestMethod]
        public void AddExponent_Overflow_Throws()
        {
            _cpu.SetAcc(MakeReal(127, B39).Value); // максимальный порядок
            ExpectThrows("ovf addexponent", () => _cpu.ArithAddExponent(1));
        }

        [TestMethod]
        public void AddExponent_Overflow_Disabled()
        {
            _cpu.SetRau((ulong)RauFlags.OvfDisable);
            _cpu.SetAcc(MakeReal(127, B39).Value);
            _cpu.ArithAddExponent(1);
            Expect("ovf disabled", MakeReal(0, B39), Word48.Zero);
        }

        // ─── Смена знака (ChangeSign) ───────────────────────────────────────────────

        [TestMethod]
        public void ChangeSign_Positive_BecomesNegative()
        {
            // 1.0 → −1.0.
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.ArithChangeSign(true);
            Expect("sgn(1.0)", MakeReal(64, -B40), Word48.Zero); // −1.0
        }

        [TestMethod]
        public void ChangeSign_Negative_BecomesPositive()
        {
            // −1.0 → 1.0.
            _cpu.SetAcc(MakeReal(64, -B40).Value);
            _cpu.ArithChangeSign(true);
            Expect("sgn(−1.0)", MakeReal(65, B39), Word48.Zero); // 1.0
        }

        [TestMethod]
        public void ChangeSign_Zero_StaysZero()
        {
            _cpu.SetAcc(0);
            _cpu.ArithChangeSign(true);
            Expect("sgn(0)", Word48.Zero, Word48.Zero);
        }

        [TestMethod]
        public void ChangeSign_NoNegate_KeepsValue()
        {
            // negateAcc = false: значение не меняется.
            _cpu.SetAcc(MakeReal(65, B39).Value);
            _cpu.ArithChangeSign(false);
            Expect("no negate", MakeReal(65, B39), Word48.Zero);
        }

        // ─── MantissaExponent: структура напрямую ───────────────────────────────────

        [TestMethod]
        public void MantissaExponent_Decode_Positive()
        {
            MantissaExponent me = new MantissaExponent(MakeReal(65, B39));
            Assert.AreEqual(65u, me.Exponent, "Порядок");
            Assert.AreEqual(B39, me.Mantissa, "Мантисса");
            Assert.IsFalse(me.IsNegative(), "Должно быть положительным");
            Assert.IsFalse(me.IsDenormal(), "Должно быть нормализованным");
        }

        [TestMethod]
        public void MantissaExponent_Decode_Negative()
        {
            // −1.0 = знак 1, мантисса в дополнительном коде.
            MantissaExponent me = new MantissaExponent(MakeReal(64, -B40));
            Assert.AreEqual(64u, me.Exponent, "Порядок");
            Assert.AreEqual(-B40, me.Mantissa, "Мантисса (доп. код)");
            Assert.IsTrue(me.IsNegative(), "Должно быть отрицательным");
        }

        [TestMethod]
        public void MantissaExponent_Negate()
        {
            MantissaExponent me = new MantissaExponent(65, B39);
            me.Negate();
            Assert.AreEqual(-B39, me.Mantissa, "Мантисса после смены знака");
        }

        [TestMethod]
        public void MantissaExponent_HighestBit()
        {
            Assert.AreEqual(-1, MantissaExponent.HighestBit(0));
            Assert.AreEqual(0, MantissaExponent.HighestBit(1));
            Assert.AreEqual(39, MantissaExponent.HighestBit(B39));
            Assert.AreEqual(38, MantissaExponent.HighestBit(B38));
        }

        [TestMethod]
        public void MantissaExponent_Multiply_Positive()
        {
            // 2^39 × 2^39 = 2^78 -> верх 41 бит: 2^38, нижние 40 бит: 0.
            MantissaExponent me = new MantissaExponent(65, B39);
            long mr = me.Multiply(B39);
            Assert.AreEqual(B38, me.Mantissa, "Верхние биты произведения");
            Assert.AreEqual(0, mr, "Младшие биты произведения");
        }

        [TestMethod]
        public void MantissaExponent_Multiply_Negative()
        {
            // −2^39 × 2^39 = −2^78 -> верх: −2^38.
            MantissaExponent me = new MantissaExponent(65, -B39);
            long mr = me.Multiply(B39);
            Assert.AreEqual(-B38, me.Mantissa, "Верхние биты (отрицательные)");
            Assert.AreEqual(0, mr, "Младшие биты");
        }

        [TestMethod]
        public void MantissaExponent_Multiply_WithRemainder()
        {
            // (3·2^38) × (2^39 + 1) = 3·2^77 + 3·2^38.
            // Верхние 41 бита -> Mantissa = 3·2^37, младшие 40 -> mr = 3·2^38.
            MantissaExponent me = new MantissaExponent(65, 3 * B38);
            long mr = me.Multiply(B39 + 1);
            Assert.AreEqual(3L << 37, me.Mantissa, "Верхние биты произведения");
            Assert.AreEqual(3L << 38, mr, "Младшие биты произведения");
        }

        [TestMethod]
        public void MantissaExponent_NormalizeToTheRight()
        {
            // Нормализация вправо: мантисса >>= 1, порядок += 1.
            MantissaExponent me = new MantissaExponent(64, B39);
            me.NormalizeToTheRight();
            Assert.AreEqual(65u, me.Exponent, "Порядок увеличен");
            Assert.AreEqual(B39 / 2, me.Mantissa, "Мантисса сдвинута вправо");
        }

        [TestMethod]
        public void MantissaExponent_IsDenormal_Boundary()
        {
            // is_denormal = (бит 41 XOR бит 42): знак и дубль-знак должны совпадать.
            // Проверка осмысленна только для значений, где бит 41 отличается от бита 40.
            Assert.IsFalse(new MantissaExponent(65, B39).IsDenormal(), "Знак=0, дубль-знак=0 -> норм.");
            Assert.IsFalse(new MantissaExponent(65, -B40).IsDenormal(), "Знак=1, дубль-знак=1 -> норм.");

            var denorm = new MantissaExponent(65, 0);
            denorm.Mantissa = B40; // бит 40 (знак)=1, бит 41 (дубль-знак)=0
            Assert.IsTrue(denorm.IsDenormal(), "Знак=1, дубль-знак=0 -> ненорм.");
        }
    }
}
