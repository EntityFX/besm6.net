using System;

namespace Besm6.Core
{
    /// <summary>
    /// Интерфейс оперативной памяти БЭСМ-6.
    /// </summary>
    public interface IMemory
    {
        /// <summary>
        /// Считывает слово из памяти по указанному адресу.
        /// </summary>
        /// <param name="address">15-битный адрес ячейки памяти.</param>
        /// <returns>48-битное слово.</returns>
        Word48 Read(uint address);

        /// <summary>
        /// Записывает слово в память по указанному адресу.
        /// </summary>
        /// <param name="address">15-битный адрес ячейки памяти.</param>
        /// <param name="word">Слово для записи.</param>
        void Write(uint address, Word48 word);

        /// <summary>
        /// Общий размер доступной памяти в словах.
        /// </summary>
        int Size { get; }
    }
}
