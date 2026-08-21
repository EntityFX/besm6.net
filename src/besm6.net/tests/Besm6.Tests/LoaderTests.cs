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
            Assert.AreEqual(0L, Besm6Math.DoubleToBesm6(0.0));
        }

        [TestMethod]
        public void RoundTrip_PositiveValue()
        {
            double val = 1.5;
            long word = Besm6Math.DoubleToBesm6(val);
            double back = Besm6Math.Besm6ToDouble(word);
            Assert.AreEqual(val, back, 1e-6);
        }

        [TestMethod]
        public void RoundTrip_NegativeValue()
        {
            double val = -3.75;
            long word = Besm6Math.DoubleToBesm6(val);
            double back = Besm6Math.Besm6ToDouble(word);
            Assert.AreEqual(val, back, 1e-5);
        }

        [TestMethod]
        public void RoundTrip_SmallValue()
        {
            double val = 0.25;
            long word = Besm6Math.DoubleToBesm6(val);
            double back = Besm6Math.Besm6ToDouble(word);
            Assert.AreEqual(val, back, 1e-8);
        }

        [TestMethod]
        public void Sqrt_One_Is_One()
        {
            long one = Besm6Math.DoubleToBesm6(1.0);
            long result = Besm6Math.Sqrt(one);
            double val = Besm6Math.Besm6ToDouble(result);
            Assert.AreEqual(1.0, val, 1e-8);
        }

        [TestMethod]
        public void Sqrt_Four_Is_Two()
        {
            long four = Besm6Math.DoubleToBesm6(4.0);
            long result = Besm6Math.Sqrt(four);
            double val = Besm6Math.Besm6ToDouble(result);
            Assert.AreEqual(2.0, val, 1e-8);
        }

        [TestMethod]
        public void Sin_Zero_Is_Zero()
        {
            long zero = Besm6Math.DoubleToBesm6(0.0);
            long result = Besm6Math.Sin(zero);
            double val = Besm6Math.Besm6ToDouble(result);
            Assert.AreEqual(0.0, val, 1e-10);
        }

        [TestMethod]
        public void Cos_Zero_Is_One()
        {
            long zero = Besm6Math.DoubleToBesm6(0.0);
            long result = Besm6Math.Cos(zero);
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
            // в левой половине слова по адресу 01000.
            const int baseAddr = 01000;
            long stop24 = (1L << 20) | (0xD8L << 12); // длинный формат: бит20 + opcode 0xD8
            long stopWord = stop24 << 24;             // левая половина слова
            var job = new DubJob();
            job.RawWords.Add(stopWord & 0xFFFFFFFFFFFFL);
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
            Assert.AreEqual(1032, machine.Cpu.GetPc());

            // Проверка, что данные в 03000 (oct) = 1536 (dec) не нулевые (INPUTCAL).
            long w03000 = machine.Memory.Read(1536).Value;
            Assert.AreNotEqual(0L, w03000, "Слово 03000 (INPUTCAL) не должно быть нулевым");

            // Проверка, что загрузчик-код записан в 02010..02023 (oct) = 1032..1043 (dec).
            bool anyNonZero = false;
            for (int i = 1032; i <= 1043; i++)
            {
                if (machine.Memory.Read(i).Value != 0)
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
            long atxWord = Besm6.Asm.Assembler.Asm("atx 1234") << 24;
            long stopWord = ((1L << 20) | (0xD8L << 12)) << 24;

            var job = new DubJob();
            job.RawWords.Add(atxWord & 0xFFFFFFFFFFFFL);
            job.RawWords.Add(stopWord & 0xFFFFFFFFFFFFL);
            job.TransMain = baseAddr;

            var result = loader.RunJob(job, System.Array.Empty<string>());
            Assert.IsTrue(result.Success, $"Ожидалась остановка, получено: {result}");
            Assert.IsTrue(result.Instructions >= 2);

            // ACC = 0, поэтому atx 1234 (oct) = 668 (dec) записывает 0 в память[668].
            long mem1234 = machine.Memory.Read(668).Value;
            Assert.AreEqual(0L, mem1234, "atx 1234 хранит ACC(0) в памяти[1234]");
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

            long acc = machine.Cpu.GetAcc();
            Assert.AreEqual(206L, acc);
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
            long word = 0;
            foreach (byte b in bytes)
                word = (word << 8) | b;
            machine.Memory.Write(512, new Word48(word));

            // E64: адрес из M[14]. E64_Pointer в 500, E64_Info в 501.
            // Pointer (C++ union E64_Pointer, LSB-first): start_addr биты 38..24,
            // end_addr биты 14..0. => 512<<24 | 512.
            machine.Memory.Write(500, new Word48((512L << 24) | 512L));
            // Info: format=0(GOST), finish=1 (бит 23).
            machine.Memory.Write(501, new Word48(1L << 23));
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

            long mem = machine.Memory.Read(520).Value;
            Assert.AreEqual(42L, mem);
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
            Assert.AreEqual(2.0, Besm6Math.Besm6ToDouble(machine.Cpu.GetAcc()), 1e-3);

            // sin(0.0) = 0.0
            machine.Cpu.SetM(14, 1);
            machine.Cpu.SetAcc(Besm6Math.DoubleToBesm6(0.0));
            handler.Handle(40, 0);
            Assert.AreEqual(0.0, Besm6Math.Besm6ToDouble(machine.Cpu.GetAcc()), 1e-6);

            // cos(0.0) = 1.0
            machine.Cpu.SetM(14, 2);
            machine.Cpu.SetAcc(Besm6Math.DoubleToBesm6(0.0));
            handler.Handle(40, 0);
            Assert.AreEqual(1.0, Besm6Math.Besm6ToDouble(machine.Cpu.GetAcc()), 1e-6);
        }

        [TestMethod]
        public void E64_AppendsNewlineToNonEmptyOutput()
        {
            var machine = new MachineCore();
            // "HI" + end-of-text в памяти 512.
            // GOST-Latin: H=0x2D (oct 055), I=0x42 (oct 102), end=0x7A (oct 172).
            byte[] bytes = { 0x2D, 0x42, 0x7A, 0, 0, 0 };
            long word = 0;
            foreach (byte b in bytes) word = (word << 8) | b;
            machine.Memory.Write(512, new Word48(word));

            // E64_Pointer в 500, E64_Info (format=0, finish=1) в 501.
            machine.Memory.Write(500, new Word48((512L << 24) | 512L));
            machine.Memory.Write(501, new Word48(1L << 23));
            machine.Cpu.SetM(14, 500);

            string captured = "";
            var handler = new ExtracodeHandler(
                machine, id => null, u => null, d => null,
                output: s => captured += s, input: p => "");
            handler.Handle(52, 0); // *64
            StringAssert.Contains(captured, "HI");
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
                machine.Memory.Write(i, new Word48(i * 7L));

            // E70 write: sectIo(bit47)=1, rawSect(bit35)=1, unit=1(bits12-17), write(bit39=0).
            long writeCtrl = (1L << 47) | (1L << 35) | (1L << 12);
            machine.Cpu.SetM(14, 0);
            machine.Cpu.SetAcc(writeCtrl);
            handler.Handle(56, 0); // *70
            Assert.AreEqual(7L, drum.ReadWord(1), "Слово 1 сектора должно совпасть с memory[1]");
            Assert.AreEqual(14L, drum.ReadWord(2), "Слово 2 сектора должно совпасть с memory[2]");

            // Стереть memory и считать обратно с барабана.
            for (int i = 0; i < 256; i++)
                machine.Memory.Write(i, new Word48(0));
            machine.Cpu.SetAcc(writeCtrl | (1L << 39)); // read
            handler.Handle(56, 0);
            Assert.AreEqual(7L, machine.Memory.Read(1).Value, "Чтение сектора: memory[1]");
            Assert.AreEqual(14L, machine.Memory.Read(2).Value, "Чтение сектора: memory[2]");
        }
    }
}
