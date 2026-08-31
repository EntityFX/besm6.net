using Besm6.Core;
using Besm6.Loader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    [TestClass]
    public class JobParserTests
    {
        [TestMethod]
        public void Parse_SimpleDub_ExtractsControlCardsAndRawWords()
        {
            var lines = new[]
            {
                "*name B compiler",
                "*tape:7/b,40",
                "*library:40",
                "*trans-main:40020",
                "/* comment */",
                "main() { printf(\"hi\"); }",
                "`0123456701234567",
                "`0000000000000014",
                "*execute",
                "*end file",
            };

            var job = JobParser.Parse(lines);

            Assert.AreEqual("B compiler", job.Name);
            Assert.AreEqual(2, job.RawWords.Count);
            // Octal "0123456701234567" = 0x053977053977
            Assert.AreEqual(0x053977053977L, job.RawWords[0]);
            Assert.AreEqual(Convert.ToInt32("14", 8), job.RawWords[1]); // 0000000000000014 (octal) = 12
            Assert.AreEqual(1, job.TapeMounts.Count);
            Assert.AreEqual(7, job.TapeMounts[0].Channel);
            Assert.AreEqual("b", job.TapeMounts[0].Name);
            Assert.AreEqual(40, job.TapeMounts[0].Zone);
            Assert.AreEqual(1, job.Libraries.Count);
            Assert.AreEqual(40, job.Libraries[0]);
            Assert.AreEqual(Convert.ToInt32("40020", 8), job.TransMain);
            Assert.AreEqual(2, job.SourceLines.Count); // "/* comment */" + "main() { ... }"
        }

        [TestMethod]
        public void Parse_RawWord_HandlesUpTo16OctalDigits()
        {
            // 16 восьмеричных цифр = 48 бит.
            var job = JobParser.Parse(new[] { "`7777777777777777" });
            Assert.AreEqual(1, job.RawWords.Count);
            Assert.AreEqual(0xFFFFFFFFFFFFL, job.RawWords[0]);
        }

        [TestMethod]
        public void Parse_SourceAndEmptyLines_Collected()
        {
            var job = JobParser.Parse(new[] { "n 10;", "", "main() { }", "*execute" });
            Assert.AreEqual(3, job.SourceLines.Count);
            Assert.AreEqual("", job.SourceLines[1]);
            Assert.AreEqual("execute", job.ControlCards[0].Directive);
        }

        [TestMethod]
        public void Parse_AssemSection_CollectsMnemonicAndRawWords()
        {
            var lines = new[]
            {
                "*name hello-assem",
                "*assem",
                "xta 1003",
                "*64",
                "stop",
                "`000201000001004",
                "`2204251423047400",
                "*end file",
            };

            var job = JobParser.Parse(lines);

            Assert.AreEqual("hello-assem", job.Name);
            // 3 мнемоники (xta, *64, stop) + 2 сырых слова (внутри секции).
            Assert.AreEqual(5, job.AssemProgram.Count);
            Assert.IsFalse(job.AssemProgram[0].IsRaw);
            Assert.AreEqual("xta 1003", job.AssemProgram[0].Text);
            // '*64' внутри секции — инструкция (экстракод), не карта управления.
            Assert.IsFalse(job.AssemProgram[1].IsRaw);
            StringAssert.Contains(job.AssemProgram[1].Text, "64");
            // 'stop' — инструкция СТОП.
            Assert.IsFalse(job.AssemProgram[2].IsRaw);
            StringAssert.Contains(job.AssemProgram[2].Text, "stop");
            // Сырые слова сохранились как есть (восьмеричные).
            Assert.IsTrue(job.AssemProgram[3].IsRaw);
            Assert.AreEqual(System.Convert.ToInt64("000201000001004", 8), job.AssemProgram[3].Value);
            Assert.IsTrue(job.AssemProgram[4].IsRaw);
            Assert.AreEqual(System.Convert.ToInt64("2204251423047400", 8), job.AssemProgram[4].Value);
            // Ни одно сырое слово не попало во вне-секционные RawWords.
            Assert.AreEqual(0, job.RawWords.Count);
            Assert.IsTrue(job.HasProgramImage);
        }

        [TestMethod]
        public void Parse_RawWordsOutsideAssem_Unchanged()
        {
            var job = JobParser.Parse(new[] { "`010100300000000", "*execute", "*end file" });
            Assert.AreEqual(1, job.RawWords.Count);
            // Сырые слова в .dub — восьмеричные.
            Assert.AreEqual(System.Convert.ToInt64("010100300000000", 8), job.RawWords[0]);
            Assert.AreEqual(0, job.AssemProgram.Count);
        }
    }

    [TestClass]
    public class CosyCodecTests
    {
        [TestMethod]
        public void EncodeCosy_PacksTrailingSpacesAndAlignsToSix()
        {
            // "abc" + 80 пробелов + '\n' -> после пакования: 'a','b','c',0xD0,'\n'
            // выравнивание до 6 -> добавляется ещё '\n'.
            byte[] result = CosyCodec.EncodeCosy("abc");

            Assert.AreEqual(0, result.Length % 6, "COSY строка должна быть кратна 6 байтам");
            Assert.AreEqual(6, result.Length);
            Assert.AreEqual(0x61, result[0]); // 'a'
            Assert.AreEqual(0x62, result[1]); // 'b'
            Assert.AreEqual(0x63, result[2]); // 'c'
            Assert.AreEqual(0x80 + 80, result[3]); // 80 упакованных пробелов
            Assert.AreEqual(0x0A, result[4]); // '\n'
            Assert.AreEqual(0x0A, result[5]); // выравнивание
        }

        [TestMethod]
        public void EncodeCosy_RoundTrip_DecodeRestoresText()
        {
            // KOI-7 использует прописные буквы, поэтому вход — в верхнем регистре.
            string input = "*NAME HELLO";
            byte[] encoded = CosyCodec.EncodeCosy(CosyCodec.Utf8ToKoi7(input));
            string? decoded = CosyCodec.DecodeCosy(encoded);
            Assert.AreEqual(input, decoded);
        }

        [TestMethod]
        public void EncodeCosy_EndFile_ProducesValidCosyLine()
        {
            // Кодировка '*end file' должна давать корректную COSY-карту
            // (кратна 6 байтам), декодирующуюся обратно в текст.
            byte[] enc = CosyCodec.EncodeCosy(CosyCodec.Utf8ToKoi7("*end file"));
            Assert.AreEqual(0, enc.Length % 6);
            string? text = CosyCodec.DecodeCosy(enc);
            Assert.AreEqual("*END FILE", text);
        }

        [TestMethod]
        public void Utf8ToKoi7_ConvertsCyrillic()
        {
            // А (U+0410) -> 'A', Б (U+0411) -> 'b', В (U+0412) -> 'B'.
            string koi7 = CosyCodec.Utf8ToKoi7("АБВ");
            Assert.AreEqual("AbB", koi7);
        }
    }

    [TestClass]
    public class Besm6MathTests
    {
        [TestMethod]
        public void Besm6ToDouble_Zero_ReturnsZero()
        {
            Assert.AreEqual(0.0, Besm6Math.Besm6ToDouble(0), 1e-20);
        }

        [TestMethod]
        public void DoubleToBesm6_Zero_ReturnsZero()
        {
            Assert.AreEqual(0UL, Besm6Math.DoubleToBesm6(0.0));
        }

        [TestMethod]
        public void RoundTrip_PositiveValue()
        {
            double val = 1.5;
            ulong word = Besm6Math.DoubleToBesm6(val);
            double back = Besm6Math.Besm6ToDouble(word);
            Assert.AreEqual(val, back, 1e-6);
        }

        [TestMethod]
        public void RoundTrip_NegativeValue()
        {
            double val = -3.75;
            ulong word = Besm6Math.DoubleToBesm6(val);
            double back = Besm6Math.Besm6ToDouble(word);
            Assert.AreEqual(val, back, 1e-5);
        }

        [TestMethod]
        public void RoundTrip_SmallValue()
        {
            double val = 0.25;
            ulong word = Besm6Math.DoubleToBesm6(val);
            double back = Besm6Math.Besm6ToDouble(word);
            Assert.AreEqual(val, back, 1e-8);
        }

        [TestMethod]
        public void Sqrt_One_Is_One()
        {
            ulong one = Besm6Math.DoubleToBesm6(1.0);
            ulong result = Besm6Math.Sqrt(one);
            double val = Besm6Math.Besm6ToDouble(result);
            Assert.AreEqual(1.0, val, 1e-8);
        }

        [TestMethod]
        public void Sqrt_Four_Is_Two()
        {
            ulong four = Besm6Math.DoubleToBesm6(4.0);
            ulong result = Besm6Math.Sqrt(four);
            double val = Besm6Math.Besm6ToDouble(result);
            Assert.AreEqual(2.0, val, 1e-8);
        }

        [TestMethod]
        public void Sin_Zero_Is_Zero()
        {
            ulong zero = Besm6Math.DoubleToBesm6(0.0);
            ulong result = Besm6Math.Sin(zero);
            double val = Besm6Math.Besm6ToDouble(result);
            Assert.AreEqual(0.0, val, 1e-10);
        }

        [TestMethod]
        public void Cos_Zero_Is_One()
        {
            ulong zero = Besm6Math.DoubleToBesm6(0.0);
            ulong result = Besm6Math.Cos(zero);
            double val = Besm6Math.Besm6ToDouble(result);
            Assert.AreEqual(1.0, val, 1e-10);
        }
    }

    [TestClass]
    public class DubnaLoaderTests
    {
        [TestMethod]
        public void RunRawWords_SingleStop_Halts()
        {
            var machine = new MachineCore();
            var loader = new DubnaLoader(machine) { InstructionLimit = 1000 };

            // Программа: единственная инструкция СТОП (opcode 0xD8, длинный формат)
            // в левой половине слова по адресу 01000 (oct) = 512 (dec).
            const int baseAddr = 512; // 01000 oct (октальные литералы в C# недоступны)
            ulong stop24 = (1UL << 20) | (0xD8UL << 12); // длинный формат: бит20 + opcode 0xD8
            ulong stopWord = stop24 << 24;             // левая половина слова
            var job = new DubJob();
            job.RawWords.Add((long)(stopWord & 0xFFFFFFFFFFFFUL));
            job.TransMain = baseAddr;

            var result = loader.RunJob(job, System.Array.Empty<string>());

            Assert.IsTrue(result.Success, $"Ожидалась остановка, получено: {result}");
            Assert.IsTrue(result.Stopped);
            Assert.IsTrue(result.Instructions >= 1);
        }

        [TestMethod]
        public void BootMsDubna_WritesMagicCodeAt02010()
        {
            var machine = new MachineCore();
            var loader = new DubnaLoader(machine) { Verbose = false };

            loader.BootMsDubna();

            // Проверка, что PC установлен в 02010 (oct) = 1032 (dec).
            Assert.AreEqual(1032UL, (ulong)machine.Cpu.GetPc());

            // Проверка, что данные в 03000 (oct) = 1536 (dec) не нулевые (INPUTCAL).
            ulong w03000 = machine.Memory.Read(1536).Value;
            Assert.AreNotEqual(0UL, w03000, "Слово 03000 (INPUTCAL) не должно быть нулевым");

            // Проверка, что загрузчик-код записан в 02010..02023 (oct) = 1032..1043 (dec).
            bool anyNonZero = false;
            for (int i = 1032; i <= 1043; i++)
            {
                if (machine.Memory.Read((uint)i).Value != 0)
                {
                    anyNonZero = true;
                    break;
                }
            }
            Assert.IsTrue(anyNonZero, "Загрузчик-код должен быть записан в 02010-02023");
        }

        [TestMethod]
        public void RunRawWords_StoreThenStop()
        {
            var machine = new MachineCore();
            var loader = new DubnaLoader(machine) { InstructionLimit = 100 };

            // 01000 (oct) = 512 (dec): atx 1234  (записать ACC в память по адресу 1234)
            // 01001 (oct) = 513 (dec): stop      (остановиться)
            int baseAddr = 512;
            ulong atxWord = Besm6.Asm.Assembler.Asm("atx 1234") << 24;
            ulong stopWord = ((1UL << 20) | (0xD8UL << 12)) << 24;

            var job = new DubJob();
            job.RawWords.Add((long)(atxWord & 0xFFFFFFFFFFFFUL));
            job.RawWords.Add((long)(stopWord & 0xFFFFFFFFFFFFUL));
            job.TransMain = baseAddr;

            var result = loader.RunJob(job, System.Array.Empty<string>());
            Assert.IsTrue(result.Success, $"Ожидалась остановка, получено: {result}");
            Assert.IsTrue(result.Instructions >= 2);

            // ACC = 0, поэтому atx 1234 (oct) = 668 (dec) записывает 0 в память[668].
            ulong mem1234 = machine.Memory.Read(668).Value;
            Assert.AreEqual(0UL, mem1234, "atx 1234 хранит ACC(0) в памяти[1234]");
        }

        [TestMethod]
        public void MountScriptTapes_MissingMonsys_ThrowsBeforeExecution()
        {
            string empty = Path.Combine(Path.GetTempPath(), "besm6_empty_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(empty);
            try
            {
                var loader = new DubnaLoader(new MachineCore(), empty);
                ProcessorException ex = Assert.Throws<ProcessorException>(
                    () => loader.MountScriptTapes(new DubJob()));
                StringAssert.Contains(ex.Message, "MONSYS");
            }
            finally
            {
                Directory.Delete(empty, recursive: true);
            }
        }

        [TestMethod]
        public void MountScriptTapes_UnknownRequestedTape_Throws()
        {
            string empty = Path.Combine(Path.GetTempPath(), "besm6_empty_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(empty);
            try
            {
                DubJob job = JobParser.Parse(new[] { "*tape:5/no-such-volume" });
                var loader = new DubnaLoader(new MachineCore(), empty);
                ProcessorException ex = Assert.Throws<ProcessorException>(() => loader.MountScriptTapes(job));
                StringAssert.Contains(ex.Message, "no-such-volume");
            }
            finally
            {
                Directory.Delete(empty, recursive: true);
            }
        }

        [TestMethod]
        public void MountScriptTapes_DoesNotAcceptLegacyFallbackAsMonsys()
        {
            string empty = Path.Combine(Path.GetTempPath(), "besm6_empty_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(empty);
            try
            {
                var loader = new DubnaLoader(new MachineCore(), empty);
                Assert.IsTrue(loader.MountTape(24, TapeImage.TapeMonsys));

                ProcessorException ex = Assert.Throws<ProcessorException>(
                    () => loader.MountScriptTapes(new DubJob()));
                StringAssert.Contains(ex.Message, "MONSYS");
            }
            finally
            {
                Directory.Delete(empty, recursive: true);
            }
        }

        [TestMethod]
        public void ReleaseTapes_CleansFileBackedProvenanceAfterLastDuplicate()
        {
            var loader = new DubnaLoader(new MachineCore());
            Assert.IsTrue(loader.MountTape(24, TapeImage.TapeMonsys));
            Assert.IsTrue(loader.MountTape(25, TapeImage.TapeMonsys));

            var provenance = (System.Collections.Generic.HashSet<TapeImage>)typeof(DubnaLoader)
                .GetField("_fileBackedTapes", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)!
                .GetValue(loader)!;
            Assert.AreEqual(2, provenance.Count);

            loader.ReleaseTapes(1L << 47);
            Assert.AreEqual(1, provenance.Count,
                "Releasing one duplicate must retain provenance for the remaining unit.");

            loader.ReleaseTapes(1L << 46);
            Assert.AreEqual(0, provenance.Count,
                "Releasing the last duplicate must remove its provenance entry.");
        }
    }

    [TestClass]
    public class ExtracodeHandlerTests
    {
        [TestMethod]
        public void E63_TimeLimit_Returns206()
        {
            var machine = new MachineCore();
            var loader = new DubnaLoader(machine);
            var handler = new ExtracodeHandler(
                machine,
                id => null,
                u => null,
                d => null,
                output: s => { });

            // M[14] = 1 (time limit request).
            machine.Cpu.SetM(14, 1);

            handler.Handle(51, 0); // 063 oct = 51 dec

            ulong acc = machine.Cpu.GetAcc().Value;
            Assert.AreEqual(206UL, acc);
        }

        [TestMethod]
        public void E64_WritesOutput()
        {
            var machine = new MachineCore();
            var loader = new DubnaLoader(machine);
            var handler = new ExtracodeHandler(
                machine,
                id => null,
                u => null,
                d => null,
                output: s => { },
                input: p => "");

            // Записать "HI" + end-of-text в память 512.
            // GOST-Latin: H=0x4D (oct 115), I=0x82 (oct 202), end=0x7A (oct 172).
            byte[] bytes = { 0x2D, 0x42, 0x7A, 0, 0, 0 };
            ulong word = 0;
            foreach (byte b in bytes)
                word = (word << 8) | b;
            machine.Memory.Write(512, new Word48(word));

            // E64: адрес из M[14]. E64_Pointer в 500, E64_Info в 501.
            // end_addr биты 14..0. => 512<<24 | 512.
            machine.Memory.Write(500, new Word48((512UL << 24) | 512UL));
            // Info: format=0(GOST), finish=1 (бит 23).
            machine.Memory.Write(501, new Word48(1UL << 23));
            machine.Cpu.SetM(14, 500);

            bool captured = false;
            handler = new ExtracodeHandler(
                machine,
                id => null,
                u => null,
                d => null,
                output: s => { captured = true; },
                input: p => "");

            handler.Handle(52, 0); // 064 oct = 52 dec
            handler.FinishOutput();
            Assert.IsTrue(captured, "E64 должен вывести текст");
        }

        [TestMethod]
        public void E75_WritesAccToMemory()
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine,
                id => null,
                u => null,
                d => null,
                output: s => { });

            // M[14] = 01010 (oct) = 520 (dec), ACC = 42.
            machine.Cpu.SetM(14, 520);
            machine.Cpu.SetAcc(42);

            handler.Handle(61, 0); // 075 oct = 61 dec

            ulong mem = machine.Memory.Read(520).Value;
            Assert.AreEqual(42UL, mem);
        }

        [TestMethod]
        public void E50_SqrtSinCos_DispatchedByM14()
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null, output: s => { });

            // sqrt(4.0) = 2.0
            machine.Cpu.SetM(14, 0);
            machine.Cpu.SetAcc(Besm6Math.DoubleToBesm6(4.0));
            handler.Handle(40, 0); // *50
            Assert.AreEqual(2.0, Besm6Math.Besm6ToDouble(machine.Cpu.GetAcc().Value), 1e-3);

            // sin(0.0) = 0.0
            machine.Cpu.SetM(14, 1);
            machine.Cpu.SetAcc(Besm6Math.DoubleToBesm6(0.0));
            handler.Handle(40, 0);
            Assert.AreEqual(0.0, Besm6Math.Besm6ToDouble(machine.Cpu.GetAcc().Value), 1e-6);

            // cos(0.0) = 1.0
            machine.Cpu.SetM(14, 2);
            machine.Cpu.SetAcc(Besm6Math.DoubleToBesm6(0.0));
            handler.Handle(40, 0);
            Assert.AreEqual(1.0, Besm6Math.Besm6ToDouble(machine.Cpu.GetAcc().Value), 1e-6);
        }

        [TestMethod]
        public void E64_AppendsNewlineToNonEmptyOutput()
        {
            var machine = new MachineCore();
            // "HI" + end-of-text в памяти 512.
            // GOST-Latin: H=0x2D (oct 055), I=0x42 (oct 102), end=0x7A (oct 172).
            byte[] bytes = { 0x2D, 0x42, 0x7A, 0, 0, 0 };
            ulong word = 0;
            foreach (byte b in bytes) word = (word << 8) | b;
            machine.Memory.Write(512, new Word48(word));

            // E64_Pointer в 500, E64_Info (format=0, finish=1) в 501.
            machine.Memory.Write(500, new Word48((512UL << 24) | 512UL));
            machine.Memory.Write(501, new Word48(1UL << 23));
            machine.Cpu.SetM(14, 500);

            string captured = "";
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null,
                output: s => captured += s, input: p => "");
            handler.Handle(52, 0); // *64
            handler.FinishOutput();
            Assert.AreEqual("HI\n", captured);
        }

        [TestMethod]
        public void E64_GostOutput_MatchesCppInitialSeparatorAndNullSubstitution()
        {
            var machine = new MachineCore();
            byte[] bytes = { 0x20, 0x60, 0x20, 0x7A, 0, 0 }; // A, unmapped, A, end
            ulong word = 0;
            foreach (byte b in bytes) word = (word << 8) | b;
            machine.Memory.Write(512, new Word48(word));
            machine.Memory.Write(500, new Word48((512UL << 24) | 512UL));
            // finish=1, skip=1 forces this call to emit the buffered line.
            // C++ starts with e64_skip_lines=0, therefore the first line has no
            // leading LF and e64() itself does not append a final LF.
            machine.Memory.Write(501, new Word48((1UL << 23) | (1UL << 20)));
            machine.Cpu.SetM(14, 500);

            string captured = "";
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null,
                output: s => captured += s, input: p => "");

            handler.Handle(52, 0);

            Assert.AreEqual("A A", captured);
        }

        [TestMethod]
        public void E57_Assign_MountsTape()
        {
            var machine = new MachineCore();
            bool mounted = false;
            long mountedId = 0;
            int mountedUnit = -1;
            var handler = new ExtracodeHandler(
                machine,
                id => null, u => null, d => null,
                output: s => { },
                mountTape: (tapeId, unit) => { mounted = true; mountedId = tapeId; mountedUnit = unit; return true; },
                findTape: (tapeId) => 0,
                releaseTapes: (mask) => { });

            // E57_ASSIGN = 0o2000 = 1024 decimal.
            long addr = 1024; // ASSIGN
            machine.Cpu.SetM(14, (uint)addr);
            ulong fakeTapeId = 0xB6FBB3E73009UL; // TapeMonsys
            machine.Cpu.SetAcc(fakeTapeId);
            machine.Cpu.SetM(13, 24); // disk unit 24

            handler.Handle(47, 0); // E57 = 47 dec (0o57)

            Assert.IsTrue(mounted, "E57 ASSIGN должен вызвать mountTape");
            Assert.AreEqual(fakeTapeId, (ulong)mountedId);
            Assert.AreEqual(24, mountedUnit);
            // ACC = disk unit.
            Assert.AreEqual(24UL, machine.Cpu.GetAcc().Value);
        }

        [TestMethod]
        public void MountTape_DifferentTapeOnOccupiedUnitReturnsFalse()
        {
            var loader = new DubnaLoader(
                new MachineCore(),
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

            Assert.IsTrue(loader.MountTape(24, TapeImage.TapeMonsys));
            Assert.IsFalse(loader.MountTape(24, TapeImage.TapeLibrar12),
                "A different tape cannot be reported as mounted while unit 030 still contains MONSYS");
        }

        [TestMethod]
        public void ReleaseTapes_UsesHighOrderAccumulatorBitForUnit030()
        {
            var loader = new DubnaLoader(
                new MachineCore(),
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

            Assert.IsTrue(loader.MountTape(24, TapeImage.TapeMonsys));

            loader.ReleaseTapes(1L << 47);

            Assert.IsTrue(loader.MountTape(24, TapeImage.TapeLibrar12),
                "BESM-6 accumulator bit 47 must release disk unit 030 for the next assignment");
        }

        [TestMethod]
        public void MountTape_RejectsUnitsOutsideCppDiskRange()
        {
            var loader = new DubnaLoader(
                new MachineCore(),
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

            Assert.Throws<ProcessorException>(() => loader.MountTape(23, TapeImage.TapeMonsys));
            Assert.Throws<ProcessorException>(() => loader.MountTape(56, TapeImage.TapeMonsys));
        }

        [TestMethod]
        public void ReleaseTapes_KeepsDuplicateTapeVisibleToE57Find()
        {
            var machine = new MachineCore();
            var loader = new DubnaLoader(
                machine,
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

            Assert.IsTrue(loader.MountTape(24, TapeImage.TapeMonsys));
            Assert.IsTrue(loader.MountTape(25, TapeImage.TapeMonsys));
            loader.ReleaseTapes(1L << 46); // disk index 1 = unit 031

            var handler = (ExtracodeHandler)typeof(DubnaLoader)
                .GetField("_extracode", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)!
                .GetValue(loader)!;
            machine.Cpu.SetM(14, 8); // E57 FIND
            machine.Cpu.SetAcc((ulong)TapeImage.TapeMonsys);
            Assert.IsTrue(handler.Handle(47, 0));

            Assert.AreEqual(24UL, machine.Cpu.GetAcc().Value,
                "Releasing one duplicate mount must leave the other unit discoverable.");
        }

        [TestMethod]
        public void E57_Assign_FailsWhenTapeNotFound()
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine,
                id => null, u => null, d => null,
                output: s => { },
                mountTape: (tapeId, unit) => false, // not found
                findTape: (tapeId) => 0,
                releaseTapes: (mask) => { });

            machine.Cpu.SetM(14, 1024); // ASSIGN
            machine.Cpu.SetAcc(0xDEADBEEF);
            machine.Cpu.SetM(13, 24);

            bool threw = false;
            try { handler.Handle(47, 0); } catch (ProcessorException) { threw = true; }
            Assert.IsTrue(threw, "E57 ASSIGN с несуществующей лентой должен бросить исключение");
        }

        [DataTestMethod]
        [DataRow(1024, false)]
        [DataRow(1088, true)]
        public void E57_Assign_PassesWritePermit(int addr, bool expectedWritePermit)
        {
            var machine = new MachineCore();
            bool? writePermit = null;
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null,
                output: s => { },
                mountTapeWithMode: (tapeId, unit, writable) =>
                {
                    writePermit = writable;
                    return true;
                });
            machine.Cpu.SetM(14, (uint)addr);
            machine.Cpu.SetM(13, 24);
            machine.Cpu.SetAcc(TapeImage.TapeMonsys);

            handler.Handle(47, 0);

            Assert.AreEqual(expectedWritePermit, writePermit);
        }

        [TestMethod]
        public void E57_Release_CallsReleaseTapes()
        {
            var machine = new MachineCore();
            ulong releasedMask = 0;
            var handler = new ExtracodeHandler(
                machine,
                id => null, u => null, d => null,
                output: s => { },
                mountTape: (tapeId, unit) => true,
                findTape: (tapeId) => 0,
                releaseTapes: (mask) => { releasedMask = (ulong)mask; });

            // E57_RELEASE = 0o4000 = 2048 decimal.
            machine.Cpu.SetM(14, 2048); // RELEASE
            ulong bitmask = (1UL << 0) | (1UL << 3); // release units 0 and 3
            machine.Cpu.SetAcc(bitmask);

            handler.Handle(47, 0);

            Assert.AreEqual(bitmask, releasedMask);
            Assert.AreEqual(0UL, machine.Cpu.GetAcc().Value, "After RELEASE, ACC should be 0");
        }

        [TestMethod]
        public void E57_ReleaseReady_DoesNotReleaseTapes()
        {
            var machine = new MachineCore();
            bool released = false;
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null,
                output: s => { },
                releaseTapes: mask => released = true);
            machine.Cpu.SetM(14, 2048 + 32); // RELEASE | READY
            machine.Cpu.SetAcc(1);

            handler.Handle(47, 0);

            Assert.IsFalse(released);
            Assert.AreEqual(0UL, machine.Cpu.GetAcc().Value);
        }

        [TestMethod]
        public void E57_Find_ReturnsUnit()
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine,
                id => null, u => null, d => null,
                output: s => { },
                mountTape: (tapeId, unit) => true,
                findTape: (tapeId) => tapeId == 0xB6FBB3E73009L ? 24 : 0,
                releaseTapes: (mask) => { });

            // addr >= 0o10 (8) and no ASSIGN/RELEASE bits → FIND.
            machine.Cpu.SetM(14, 8); // 0o10 oct = 8 dec
            ulong fakeTapeId = 0xB6FBB3E73009UL;
            machine.Cpu.SetAcc(fakeTapeId);

            handler.Handle(47, 0);

            Assert.AreEqual(24UL, machine.Cpu.GetAcc().Value, "FIND должен вернуть unit");
        }

        [TestMethod]
        public void E57_Find_NotFound_ReturnsZero()
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine,
                id => null, u => null, d => null,
                output: s => { },
                mountTape: (tapeId, unit) => true,
                findTape: (tapeId) => 0, // not found
                releaseTapes: (mask) => { });

            machine.Cpu.SetM(14, 8); // FIND
            machine.Cpu.SetAcc(0x12345);

            handler.Handle(47, 0);
            Assert.AreEqual(0UL, machine.Cpu.GetAcc().Value);
        }

        [TestMethod]
        public void E57_FileVolumeOpen_AcceptsLocalDisc()
        {
            const ulong key = 0xD38EA0800000UL;
            const ulong discLocal = 0xB2F8E1B00000UL;
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null, output: s => { });
            machine.Memory.Write(101, new Word48(discLocal));
            machine.Cpu.SetM(14, 0x7FFF);
            machine.Cpu.SetAcc(key | 100UL); // VOLUME_OPEN, info address 100

            handler.Handle(47, 0);

            Assert.AreEqual(0UL, machine.Cpu.GetAcc().Value);
        }

        [TestMethod]
        public void E57_FileRequest_WithWrongAccessKeyThrows()
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null, output: s => { });
            machine.Cpu.SetM(14, 0x7FFF);
            machine.Cpu.SetAcc(100);

            try
            {
                handler.Handle(47, 0);
            }
            catch (ProcessorException ex)
            {
                Assert.AreEqual("Wrong access key in *57 77777", ex.Message);
                return;
            }

            Assert.Fail("E57 77777 обязан проверять ключ доступа");
        }

        [TestMethod]
        public void E57_SpecialAddr7_ThrowsException()
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null, output: s => { });

            machine.Cpu.SetM(14, 7); // "task paused waiting for tape"
            bool threw = false;
            try { handler.Handle(47, 0); } catch (ProcessorException) { threw = true; }
            Assert.IsTrue(threw, "E57 addr=7 должен бросить исключение");
        }

        [TestMethod]
        public void E57_SpecialAddr2_PlotterNoOp()
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null, output: s => { });

            machine.Cpu.SetM(14, 2); // Calcomp plotter
            machine.Cpu.SetAcc(0x1234);
            handler.Handle(47, 0);
            Assert.AreEqual(0UL, machine.Cpu.GetAcc().Value, "E57 addr=2 (plotter) → ACC=0");
        }

        [TestMethod]
        public void E65_Switch_ReturnsSwitchValue()
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null, output: s => { });

            // addr=1 → switch 1 → 0.
            machine.Cpu.SetM(14, 1);
            handler.Handle(53, 0); // E65 = 53 dec
            Assert.AreEqual(0UL, machine.Cpu.GetAcc().Value);

            // addr=322 → 1024.
            machine.Cpu.SetM(14, 322);
            handler.Handle(53, 0);
            Assert.AreEqual(1024UL, machine.Cpu.GetAcc().Value);
        }

        [TestMethod]
        public void E67_Jump_SetsPC()
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null, output: s => { });

            // E67: word at M[14], PC = (word >> 24) & 0x7FFF.
            int targetAddr = 1000;
            ulong word = (ulong)targetAddr << 24;
            machine.Memory.Write(200, new Word48(word));
            machine.Cpu.SetM(14, 200);

            handler.Handle(55, 0); // E67 = 55 dec
            Assert.AreEqual(targetAddr, (int)machine.Cpu.GetPc());
        }

        [TestMethod]
        public void E67_FetchWatch_ReturnsToContinuationBeforeExecutingWord()
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null, output: s => { });

            const uint controlAddress = 200;
            const uint transferAddress = 300;
            const uint watchAddress = 64; // 0100 octal
            const uint continuation = 77;
            ulong control = ((ulong)transferAddress << 24) | watchAddress;
            machine.Memory.Write(controlAddress, new Word48(control));
            machine.Memory.Write(transferAddress,
                new Word48(Besm6.Asm.Assembler.Asm("пб 100, сч 0")));
            machine.Memory.Write(watchAddress,
                new Word48(Besm6.Asm.Assembler.Asm("стоп 12345(6), сч 0")));
            machine.Cpu.SetM(14, controlAddress);
            machine.Cpu.SetPc(continuation);

            handler.Handle(55, 0);
            Assert.AreEqual(transferAddress, machine.Cpu.GetPc());
            Assert.IsFalse(machine.Cpu.Step());
            Assert.AreEqual(watchAddress, machine.Cpu.GetPc());

            Assert.IsFalse(machine.Cpu.Step(), "watchpoint должен прервать выборку до STOP");
            Assert.AreEqual(continuation, machine.Cpu.GetPc());
            Assert.IsFalse(machine.Cpu.RightInstruction);
        }

        [DataTestMethod]
        [DataRow(1, "зп 500, сч 0")]
        [DataRow(2, "сч 500, сч 0")]
        public void E67_MemoryWatch_ReturnsBeforeAccess(int mode, string instruction)
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null, output: s => { });

            const uint controlAddress = 200;
            const uint transferAddress = 300;
            const uint watchAddress = 320; // 0500 octal
            const uint continuation = 77;
            const ulong initialMemory = 0x123456789ABCUL;
            const ulong initialAcc = 0xABCDEF012345UL;
            ulong control = ((ulong)transferAddress << 24) | ((ulong)mode << 20) | watchAddress;
            machine.Memory.Write(controlAddress, new Word48(control));
            machine.Memory.Write(transferAddress,
                new Word48(Besm6.Asm.Assembler.Asm(instruction)));
            machine.Memory.Write(watchAddress, new Word48(initialMemory));
            machine.Cpu.SetAcc(initialAcc);
            machine.Cpu.SetM(14, controlAddress);
            machine.Cpu.SetPc(continuation);

            handler.Handle(55, 0);
            Assert.IsFalse(machine.Cpu.Step());

            Assert.AreEqual(continuation, machine.Cpu.GetPc());
            Assert.AreEqual(initialMemory, machine.Memory.Read(watchAddress).Value,
                "перехват записи должен происходить до изменения памяти");
            Assert.AreEqual(initialAcc, machine.Cpu.GetAcc().Value,
                "перехват чтения должен происходить до изменения ACC");
            Assert.IsFalse(machine.Cpu.RightInstruction);
        }

        [TestMethod]
        public void E67_BadWatchMode_Throws()
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null, output: s => { });
            machine.Memory.Write(200, new Word48(((ulong)300 << 24) | ((ulong)3 << 20) | 100));
            machine.Cpu.SetM(14, 200);

            try
            {
                handler.Handle(55, 0);
            }
            catch (ProcessorException ex)
            {
                Assert.AreEqual("Bad debug watchpoint mode", ex.Message);
                return;
            }

            Assert.Fail("Режим E67=3 обязан бросать ProcessorException");
        }

        [TestMethod]
        public void E76_Unimplemented_Throws()
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null, output: s => { });

            // addr=5 → unimplemented (not 0/1, < 10).
            machine.Cpu.SetM(14, 5);
            bool threw = false;
            try { handler.Handle(62, 0); } catch (ProcessorException) { threw = true; }
            Assert.IsTrue(threw, "E76 addr=5 (unimplemented) должен бросить исключение");
        }

        [TestMethod]
        public void E76_ZeroAndOne_NoOp()
        {
            var machine = new MachineCore();
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null, output: s => { });

            // addr=0 and addr=1 are no-ops.
            machine.Cpu.SetM(14, 0);
            handler.Handle(62, 0); // should not throw
            machine.Cpu.SetM(14, 1);
            handler.Handle(62, 0); // should not throw
            Assert.IsTrue(true); // reached = no exception
        }

        [TestMethod]
        public void E70_DrumSector_WriteReadRoundTrip()
        {
            var machine = new MachineCore();
            var drum = new TapeImage(1, new byte[TapeImage.DrumNWords * 6], readOnly: false);
            var handler = new ExtracodeHandler(
                machine, id => null, u => null,
                drumByUnit: u => u == 1 ? drum : null);

            // Заполнить memory[0..255].
            for (int i = 0; i < 256; i++)
                machine.Memory.Write((uint)i, new Word48((ulong)i * 7UL));

            // E70 write: sectIo(bit47)=1, rawSect(bit35)=1, unit=1(bits12-17), write(bit39=0).
            ulong writeCtrl = (1UL << 47) | (1UL << 35) | (1UL << 12);
            machine.Cpu.SetM(14, 0);
            machine.Cpu.SetAcc(writeCtrl);
            handler.Handle(56, 0); // *70
            Assert.AreEqual(7L, drum.ReadWord(1), "Слово 1 сектора должно совпасть с memory[1]");
            Assert.AreEqual(14L, drum.ReadWord(2), "Слово 2 сектора должно совпасть с memory[2]");

            // Стереть memory и считать обратно с барабана.
            for (int i = 0; i < 256; i++)
                machine.Memory.Write((uint)i, new Word48(0));
            machine.Cpu.SetAcc(writeCtrl | (1UL << 39)); // read
            handler.Handle(56, 0);
            Assert.AreEqual(7UL, machine.Memory.Read(1).Value, "Чтение сектора: memory[1]");
            Assert.AreEqual(14UL, machine.Memory.Read(2).Value, "Чтение сектора: memory[2]");
        }
    }
}
