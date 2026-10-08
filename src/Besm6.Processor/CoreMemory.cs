using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Besm6.Core
{
    /// <summary>
    /// Реализация основной оперативной памяти БЭСМ-6.
    /// Банки сохраняют прежнюю нумерацию; слова хранятся в одном массиве.
    /// </summary>
    public class CoreMemory : IMemory
    {
        private readonly Word48[] _words;
        private const uint NumBanks = 8;
        
        public int Size => _words.Length;

        public CoreMemory(uint size = 32768)
        {
            if (size < 0) throw new ArgumentException("Size cannot be negative");
            if (size % NumBanks != 0)
                throw new ArgumentException($"Memory size must be divisible by the number of banks ({NumBanks})");

            _words = new Word48[size];
        }

        public Word48 Read(uint address)
        {
            Word48[] words = _words;
            if (address >= (uint)words.Length)
                ThrowAccessViolation(address);
            return words[address];
        }

        public void Write(uint address, Word48 word)
        {
            Word48[] words = _words;
            if (address >= (uint)words.Length)
                ThrowAccessViolation(address);
            words[address] = word;
        }

        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowAccessViolation(uint address) =>
            throw new IndexOutOfRangeException($"Memory access violation at address 0x{address:X5} (Bank {address % NumBanks})");

        // Метод для симуляции задержек доступа при конфликтах в банках
        public long GetAccessTimeNs(int address)
        {
            // В реальном БЭСМ-6 время цикла 2 мкс, время выборки 0,9 мкс.
            // Здесь мы просто возвращаем базовое время выборки.
            return 900; 
        }
    }
}
