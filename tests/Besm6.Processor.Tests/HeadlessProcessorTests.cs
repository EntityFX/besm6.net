using Besm6.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    /// <summary>
    /// Headless-проверка: процессор БЭСМ-6 из сборки Besm6.Processor
    /// работает без MachineCore/Loader (plans/refactor.md, критерий готовности).
    /// </summary>
    [TestClass]
    public sealed class HeadlessProcessorTests
    {
        /// <summary>
        /// Минимальная IMemory для unit-теста.
        /// </summary>
        private sealed class TestMemory : IMemory
        {
            private readonly Word48[] _words;
            public TestMemory(int size) { _words = new Word48[size]; }
            public int Size => _words.Length;
            public Word48 Read(uint address) => _words[address];
            public void Write(uint address, Word48 word) => _words[address] = word;
        }

        [TestMethod]
        public void Processor_Runs_Without_Monolith()
        {
            var memory = new TestMemory(64);
            var cpu = new Processor(memory);

            // XTA 2 — погрузка A := X (opcode 8, reg 0, addr 2).
            // Раскладка старших 24 бит слова (rk): reg = б.47..44, opcode = б.41..36,
            // addr = б.35..24 (см. InstructionExecutor.ExtractFields).
            // Адрес 0 в БЭСМ-6 зарезервирован, поэтому данные кладем в ячейку 2.
            memory.Write(2, new Word48(5UL));
            memory.Write(1, new Word48((8UL << 36) | (2UL << 24)));
            cpu.SetK(1);

            bool stopped = cpu.Step();

            Assert.IsFalse(stopped, "одна команда XTA не должна останавливать машину");
            Assert.AreEqual(5UL, cpu.GetA().Value);
            // После левой половины K не сдвигается (смена половины — через флаг);
            // K+=1 произойдет после выполнения правой половины.
            Assert.AreEqual(1UL, cpu.GetK());
        }
    }
}
