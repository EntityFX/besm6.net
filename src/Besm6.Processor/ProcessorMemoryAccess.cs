using System;

namespace Besm6.Core
{
    /// <summary>
    /// Доступ процессора к памяти: чтение команды (fetch), чтение и запись данных,
    /// с проверкой memory-watchpoints. Вынесено из Processor.cs (Этап 4 рефакторинга).
    /// </summary>
    internal sealed class ProcessorMemoryAccess
    {
        private readonly ProcessorDebugWatch _debugWatch;
        private readonly IMemory _memory;

        internal ProcessorMemoryAccess(ProcessorDebugWatch debugWatch, IMemory memory)
        {
            _debugWatch = debugWatch;
            _memory = memory;
        }

        /// <summary>Читает слово по адресу команды; запрещает переход по нулевому адресу.</summary>
        internal ulong MemFetch(ulong addr)
        {
            addr &= 0x7FFF;
            if (addr == 0)
                throw new ProcessorException("Jump to zero");
            return _memory.Read((uint)addr).Value;
        }

        /// <summary>Читает слово данных с проверкой load-watchpoint (mode=2).</summary>
        internal ulong MemLoad(uint addr)
        {
            addr &= 0x7FFF;
            if (_debugWatch.DebugCheckMemory(addr, 2))
                throw new Processor.DebugWatchAbortException();
            if (addr == 0)
                return 0;
            return _memory.Read(addr).Value;
        }

        /// <summary>Записывает слово данных с проверкой store-watchpoint (mode=1).</summary>
        internal void MemStore(uint addr, ulong val)
        {
            addr &= 0x7FFF;
            if (_debugWatch.DebugCheckMemory(addr, 1))
                throw new Processor.DebugWatchAbortException();
            if (addr == 0)
                return;
            _memory.Write(addr, new Word48(val));
        }
    }
}
