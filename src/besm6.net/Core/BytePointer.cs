using System;

namespace Besm6.Core
{
    /// <summary>
    /// Байтовый указатель (порт dubna/memory.h BytePointer).
    /// Чтение/запись 8-бит символов внутри 48-битного слова.
    /// 6 символов по 8 бит на слово: byte_index 0..5.
    /// Символ byte_index=0 в старших битах (shift 40), byte_index=5 в младших (shift 0).
    /// </summary>
    public struct BytePointer
    {
        private readonly IMemory _memory;
        public int WordAddr;
        public int ByteIndex;

        public BytePointer(IMemory memory, int wordAddr, int byteIndex = 0)
        {
            _memory = memory;
            WordAddr = wordAddr;
            ByteIndex = byteIndex;
        }

        /// <summary>Читает байт на текущей позиции без инкремента.</summary>
        public byte Peek()
        {
            long word = _memory.Read(WordAddr).Value;
            int shift = 40 - ByteIndex * 8;
            return (byte)((word >> shift) & 0xFF);
        }

        /// <summary>Читает байт и инкрементирует.</summary>
        public byte Get()
        {
            byte ch = Peek();
            Increment();
            return ch;
        }

        /// <summary>Записывает байт и инкрементирует.</summary>
        public void Put(byte ch)
        {
            long word = _memory.Read(WordAddr).Value;
            int shift = 40 - ByteIndex * 8;
            long mask = 0xFFL << shift;
            word = (word & ~mask) | ((long)ch << shift);
            _memory.Write(WordAddr, new Word48(word));
            Increment();
        }

        /// <summary>Инкрементирует указатель.</summary>
        public void Increment()
        {
            ByteIndex++;
            if (ByteIndex == 6)
            {
                ByteIndex = 0;
                WordAddr = (WordAddr + 1) & 0x7FFF;
            }
        }
    }
}