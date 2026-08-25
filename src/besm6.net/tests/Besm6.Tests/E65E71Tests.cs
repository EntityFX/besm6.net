using System;
using System.IO;
using System.Text;
using Besm6.Core;
using Besm6.Loader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    /// <summary>
    /// Тесты экстракодов *65 (выключатели/таблица ALLTOISO) и *71 (терминал/перфоратор)
    /// после переноса логики из C++-референса (dubna/extracode.cpp).
    /// </summary>
    [TestClass]
    public class E65E71Tests
    {
        private const int E65 = 53; // 065 oct
        private const int E71 = 57; // 071 oct

        // ─── E65 ────────────────────────────────────────────────────────────

        [TestMethod]
        public void E65_PultTumblers_1to7_ReturnZero()
        {
            for (int addr = 1; addr <= 7; addr++)
            {
                var machine = new MachineCore();
                var handler = MakeHandler(machine);
                machine.Cpu.SetM(14, (uint)addr);
                handler.Handle(E65, 0);
                Assert.AreEqual(0UL, machine.Cpu.GetAcc().Value, $"addr=0{Convert.ToString(addr, 8)}");
            }
        }

        [TestMethod]
        public void E65_Case0526_ReturnsAllToIsoTableAddress()
        {
            // C++ case 0526 (342 dec): ACC = 06000 (3072 dec) — адрес таблицы ALLTOISO.
            var machine = new MachineCore();
            var handler = MakeHandler(machine);
            machine.Cpu.SetM(14, 342);
            handler.Handle(E65, 0);
            Assert.AreEqual(3072UL, machine.Cpu.GetAcc().Value);
        }

        [TestMethod]
        public void E65_BitShiftRange_0700_0757()
        {
            // C++: if (addr >= 0700 && addr < 0760) ACC = 1 << (0757 - addr).
            // Все литералы — десятичные (октальные литералы в C# недоступны).
            CheckE65Shift(448, 1UL << 47); // 0700 oct = 448 dec
            CheckE65Shift(470, 1UL << 25); // 0726 oct = 470 dec
            CheckE65Shift(495, 1UL << 0);  // 0757 oct = 495 dec
        }

        private static void CheckE65Shift(int addr, ulong expected)
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);
            machine.Cpu.SetM(14, (uint)addr);
            handler.Handle(E65, 0);
            Assert.AreEqual(expected, machine.Cpu.GetAcc().Value, $"addr=0{Convert.ToString(addr, 8)}");
        }

        [TestMethod]
        public void E65_AllToIso_TableRange()
        {
            // C++: if (addr >= 06000 && addr < 06000+128) ACC = all_to_iso[addr-06000].
            // 06000 oct = 3072 dec.
            int baseAddr = 3072;
            for (int idx = 0; idx < 128; idx++)
            {
                var machine = new MachineCore();
                var handler = MakeHandler(machine);
                machine.Cpu.SetM(14, (uint)(baseAddr + idx));
                handler.Handle(E65, 0);
                Assert.AreEqual((ulong)CosyCodec.AllToIso[idx], machine.Cpu.GetAcc().Value, $"idx={idx}");
            }
        }

        [TestMethod]
        public void E65_AllToIso_KnownValues()
        {
            AssertE65AllToIso(3072 + 0, 0xF00F00300000UL);   // GOST=0360
            AssertE65AllToIso(3072 + 1, 0xF10F5B312001UL);   // GOST=0361
            AssertE65AllToIso(3072 + 127, 0x400FC000007FUL); // последняя запись
        }

        private static void AssertE65AllToIso(int addr, ulong expected)
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);
            machine.Cpu.SetM(14, (uint)addr);
            handler.Handle(E65, 0);
            Assert.AreEqual(expected, machine.Cpu.GetAcc().Value, $"addr=0{Convert.ToString(addr, 8)}");
        }

        [TestMethod]
        public void E65_Unimplemented_Throws()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);
            machine.Cpu.SetM(14, 200); // 200 dec (0310 oct) — не в любом case/диапазоне
            Assert.Throws<ProcessorException>(() => handler.Handle(E65, 0));
        }

        // ─── E71 ────────────────────────────────────────────────────────────

        [TestMethod]
        public void E71_Flag4_TerminalOutput()
        {
            var machine = new MachineCore();
            var captured = new StringBuilder();
            var handler = MakeHandler(machine, output: s => captured.Append(s));

            // В памяти по адресу 100: "HI\0" (KOI-7, строчные латинские в KOI-7 не ASCII-совместимы).
            machine.Memory.Write(100, new Word48(0x484900000000UL));

            // Контрольное слово: flags=4, start_reg=0, start=100, end_reg=0, end=110.
            ulong ctrl = (4UL << 39) | (100UL << 24) | 110UL;
            machine.Memory.Write(200, new Word48(ctrl));
            machine.Cpu.SetM(14, 200); // M[16] = адрес контрольного слова
            machine.Cpu.SetM(0, 0);    // start_reg/end_reg = 0

            handler.Handle(E71, 0);

            Assert.AreEqual("HI\n", captured.ToString());
        }

        [TestMethod]
        public void E71_Flag6_TerminalInput()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine, input: p => "AB");

            // Контрольное слово: flags=6, start=100, end=110.
            ulong ctrl = (6UL << 39) | (100UL << 24) | 110UL;
            machine.Memory.Write(200, new Word48(ctrl));
            machine.Cpu.SetM(14, 200);
            machine.Cpu.SetM(0, 0);

            handler.Handle(E71, 0);

            ulong w = machine.Memory.Read(100).Value;
            Assert.AreEqual((byte)'A', (byte)((w >> 40) & 0xFF), "байт 0 = 'A'");
            Assert.AreEqual((byte)'B', (byte)((w >> 32) & 0xFF), "байт 1 = 'B'");
            Assert.AreEqual(0x00, (byte)((w >> 24) & 0xFF), "байт 2 = NUL");
        }

        [TestMethod]
        public void E71_Flag1_Punch_CreatesBrailleFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "besm6_punch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var machine = new MachineCore(32768, dir);
                var handler = MakeHandler(machine);

                // Одна перфокарта = 24 слова в адресе 100..123.
                for (int i = 0; i < 24; i++)
                    machine.Memory.Write((uint)(100 + i), new Word48((ulong)i * 7UL));

                // flags=1, start=100, end=123 → (123-100+1)=24 кратно 24.
                ulong ctrl = (1UL << 39) | (100UL << 24) | 123UL;
                machine.Memory.Write(200, new Word48(ctrl));
                machine.Cpu.SetM(14, 200);
                machine.Cpu.SetM(0, 0);

                handler.Handle(E71, 0);
                machine.Puncher.Finish();

                string punchOut = Path.Combine(dir, "punch.out");
                Assert.IsTrue(File.Exists(punchOut), "punch.out должен быть создан");
                Assert.IsTrue(new FileInfo(punchOut).Length > 0, "punch.out не должен быть пустым");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [TestMethod]
        public void E71_Flag1_FractionalCards_Throws()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            // (end-start+1) = 25 — не кратно 24.
            ulong ctrl = (1UL << 39) | (100UL << 24) | 124UL;
            machine.Memory.Write(200, new Word48(ctrl));
            machine.Cpu.SetM(14, 200);
            machine.Cpu.SetM(0, 0);

            Assert.Throws<ProcessorException>(() => handler.Handle(E71, 0));
        }

        [TestMethod]
        public void E71_UnknownFlag_NoOp()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            // flags=2 (неизвестный) — в C++ бездействие, не бросает.
            ulong ctrl = (2UL << 39) | (100UL << 24) | 110UL;
            machine.Memory.Write(200, new Word48(ctrl));
            machine.Cpu.SetM(14, 200);
            machine.Cpu.SetM(0, 0);

            handler.Handle(E71, 0); // не должен бросить
        }

        // ─── Puncher (unit) ─────────────────────────────────────────────────

        [TestMethod]
        public void Puncher_SingleCard_BrailleContent()
        {
            string dir = Path.Combine(Path.GetTempPath(), "besm6_puncher_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var mem = new CoreMemory(4096);
                // Заполним одну карточку (24 слова) байтами 0..143.
                var bp = new BytePointer(mem, 0);
                for (int i = 0; i < 144; i++) bp.Put((byte)i);

                var puncher = new Puncher(mem, dir);
                puncher.Punch(0, 23);
                puncher.Finish();

                string punchOut = Path.Combine(dir, "punch.out");
                Assert.IsTrue(File.Exists(punchOut));
                string content = File.ReadAllText(punchOut, new UTF8Encoding(false));
                // 3 строки по 40 braille-символов + пустая строка.
                var lines = content.Split('\n');
                Assert.IsTrue(lines.Length >= 3, "ожидается минимум 3 строки braille");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // ─── Вспомогательные ────────────────────────────────────────────────

        private static ExtracodeHandler MakeHandler(
            MachineCore machine,
            Action<string>? output = null,
            Func<string, string>? input = null)
        {
            return new ExtracodeHandler(
                machine,
                diskByTapeId: id => null,
                diskByUnit: u => null,
                drumByUnit: u => null,
                output: output ?? (s => { }),
                input: input ?? (p => ""));
        }
    }
}