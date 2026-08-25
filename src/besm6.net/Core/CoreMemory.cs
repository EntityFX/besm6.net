using System;

namespace Besm6.Core
{
    /// <summary>
    /// Реализация основной оперативной памяти БЭСМ-6.
    /// Поддерживает многоблочную структуру (8 независимых блоков) для моделирования параллелизма.
    /// </summary>
    public class CoreMemory : IMemory
    {
        private readonly Word48[][] _banks;
        private readonly uint _numBanks = 8;
        private readonly uint _wordsPerBank;
        
        public int Size => (int)(_numBanks * _wordsPerBank);

        public CoreMemory(uint size = 32768)
        {
            if (size < 0) throw new ArgumentException("Size cannot be negative");
            if (size % _numBanks != 0)
                throw new ArgumentException($"Memory size must be divisible by the number of banks ({_numBanks})");

            _wordsPerBank = size / _numBanks;
            _banks = new Word48[_numBanks][];
            for (int i = 0; i < _numBanks; i++)
            {
                _banks[i] = new Word48[_wordsPerBank];
            }
        }

        public Word48 Read(uint address)
        {
            uint bankIndex = address % _numBanks;
            uint offset = address / _numBanks;

            if (offset < 0 || offset >= _wordsPerBank)
                throw new IndexOutOfRangeException($"Memory access violation at address 0x{address:X5} (Bank {bankIndex})");

            return _banks[bankIndex][offset];
        }

        public void Write(uint address, Word48 word)
        {
            uint bankIndex = address % _numBanks;
            uint offset = address / _numBanks;

            if (offset < 0 || offset >= _wordsPerBank)
                throw new IndexOutOfRangeException($"Memory access violation at address 0x{address:X5} (Bank {bankIndex})");

            _banks[bankIndex][offset] = word;
        }

        // Метод для симуляции задержек доступа при конфликтах в банках
        public long GetAccessTimeNs(int address)
        {
            // В реальном БЭСМ-6 время цикла 2 мкс, время выборки 0,9 мкс.
            // Здесь мы просто возвращаем базовое время выборки.
            return 900; 
        }
    }
}