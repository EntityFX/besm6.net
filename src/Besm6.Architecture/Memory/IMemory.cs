using Besm6.Architecture.Numerics;
using System;

namespace Besm6.Architecture.Memory
{
    /// <summary>
    /// Общий транспорт 48-битных слов. Домен адресов и побочные эффекты задаются
    /// ролью: IHostMemory, IOperandMemory или IInstructionMemory.
    /// </summary>
    public interface IMemory
    {
        /// <summary>
        /// Считывает слово из памяти по указанному адресу.
        /// </summary>
        /// <param name="address">Адрес в домене выбранной роли памяти.</param>
        /// <returns>48-битное слово.</returns>
        Word48 Read(uint address);

        /// <summary>
        /// Записывает слово в память по указанному адресу.
        /// </summary>
        /// <param name="address">Адрес в домене выбранной роли памяти.</param>
        /// <param name="word">Слово для записи.</param>
        void Write(uint address, Word48 word);

        /// <summary>
        /// Общий размер доступной памяти в словах.
        /// </summary>
        int Size { get; }
    }
}
