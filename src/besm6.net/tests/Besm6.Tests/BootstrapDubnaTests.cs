using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Besm6.Core;
using Besm6.Loader;

namespace Besm6.Tests
{
    /// <summary>
    /// Bootstrap validation (PHASE C): после BootMsDubna() raw-память
    /// 02010..02023 oct (1032..1043 dec) должна БИТ-в-бит совпадать с C++
    /// референсом (Machine::boot_ms_dubna, ref/machine.cpp:930-976,
    /// system_load_list_flag = 0). Ожидаемые значения сгенерированы С ПОМОЩЬЮ
    /// C++ ground-truth ассемблера (tools/bootstrap_words.cpp, besm6_asm из
    /// ref/assembler.cpp) — это независимый от C# Asm эталон.
    ///
    /// Дополнительно: начальное CPU state ДО первой инструкции:
    /// PC = 02010 oct (1032 dec), half = LEFT, ACC = RMR = 0, RAU = 0, MOD = 0,
    /// M[0..15] = 0 (C++: Processor-конструктор + cpu.set_pc(02010)).
    /// </summary>
    [TestClass]
    public class BootstrapDubnaTests
    {
        private MachineCore _machine;
        private DubnaLoader _loader;

        [TestInitialize]
        public void Setup()
        {
            _machine = new MachineCore();
            _loader = new DubnaLoader(_machine) { Verbose = false };
        }

        [TestMethod]
        public void BootMsDubna_WordsMatchCppGroundTruth()
        {
            // Адрес, ожидаемое 48-битное слово (C++ besm6_asm), источник (для людей).
            var expected = new (uint addr, ulong word, string src)[]
            {
                (1032, 0x1A7FFB038602UL, "vtm -5(1),     *70 3002"),
                (1033, 0x0080FF000608UL, "xta 377,       atx 3010"),
                (1034, 0x0080F3000040UL, "xta 363,       atx 100"),
                (1035, 0xFA5701090000UL, "vtm 53401(17), utc"),
                (1036, 0x138608090000UL, "*70 3010(1),   utc"),
                (1037, 0x1F840C02200FUL, "vlm 2014(1),   ita 17"),
                (1038, 0x0001CE0381CFUL, "atx 716,       *70 717"),
                (1039, 0x00800F02000EUL, "xta 17,        ati 16"),
                (1040, 0xE0000200B601UL, "atx 2(16),     arx 3001"),
                (1041, 0x00000F008600UL, "atx 17,        xta 3000"),
                (1042, 0xE00000DA03BBUL, "atx (16),      vtm 1673(15)"),
                (1043, 0xFC0000090000UL, "uj (17),       utc"),
            };

            var table = new (uint addr, ulong word)[]
            {
                (1536, 183533445462124UL), // 03000 'INPUTCAL'
                (1537, 8UL),               // 03001
                (1538, 141562122145921UL), // 03002 инициатор
                (1539, 65536UL),           // 03003 ТРП
                (1540, 824633790471UL),    // 03004 каталоги
                (1541, 69632UL),           // 03005 временный
                (1542, 824633790472UL),    // 03006 библиотеки
                (1543, 69633UL),           // 03007 (физ. и мат.)
                (1544, 824633790493UL),    // 03010 /MONTRAN
            };

            _loader.BootMsDubna();

            var sb = new System.Text.StringBuilder();
            foreach (var (addr, word, src) in expected)
            {
                ulong actual = _machine.Memory.Read(addr).Value & 0xFFFF_FFFF_FFFFUL;
                if (actual != word)
                    sb.Append($"  addr {addr} (0{Convert.ToString(addr, 8)}): expected {word:X12} ({src}), actual {actual:X12}\n");
            }
            foreach (var (addr, word) in table)
            {
                ulong actual = _machine.Memory.Read(addr).Value & 0xFFFF_FFFF_FFFFUL;
                if (actual != word)
                    sb.Append($"  addr {addr} (0{Convert.ToString(addr, 8)}): expected {word:X12}, actual {actual:X12}\n");
            }
            Assert.IsTrue(sb.Length == 0,
                "Bootstrap raw memory MISMATCH с C++ ground truth:\n" + sb);
        }

        [TestMethod]
        public void BootMsDubna_InitialCpuState()
        {
            _loader.BootMsDubna();

            var cpu = _machine.Cpu;
            // C++: cpu.set_pc(02010); right_instr_flag = false (reset state).
            Assert.AreEqual(1032u, cpu.GetPc(), "PC должен быть 02010 oct = 1032 dec");
            Assert.IsFalse(cpu.OnRightInstruction, "half = LEFT (правая половина ещё не исполнялась)");
            Assert.AreEqual(0UL, cpu.GetAcc().Value & 0xFFFF_FFFF_FFFFUL, "ACC = 0");
            Assert.AreEqual(0UL, cpu.GetRmr().Value & 0xFFFF_FFFF_FFFFUL, "RMR = 0");
            Assert.AreEqual(0u, cpu.GetRau(), "RAU = 0 (нет режима)");
            Assert.AreEqual(0L, cpu.Mod, "MOD = 0");
            Assert.IsFalse(cpu.ApplyModReg, "apply_mod_reg = false");
            for (int i = 0; i < 16; i++)
                Assert.AreEqual(0u, cpu.GetM(i), $"M[{i}] = 0");
        }
    }
}
