using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

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
        private readonly CoreMemory? _coreMemory;
        private readonly IInstructionMemory? _instructionMemory;
        private readonly bool _allowZeroInstructionAddress;
        internal bool IsPlainCoreMemory { get; }
        internal MappedMemoryBackend? MappedBackend => _memory as MappedMemoryBackend;

        internal ProcessorMemoryAccess(ProcessorDebugWatch debugWatch, IMemory memory, bool allowZeroInstructionAddress = false)
        {
            _allowZeroInstructionAddress = allowZeroInstructionAddress;
            _debugWatch = debugWatch;
            _memory = memory;
            // An IMemory override may observe each read or change CPU hooks.
            IsPlainCoreMemory = memory.GetType() == typeof(CoreMemory);
            _coreMemory = IsPlainCoreMemory ? (CoreMemory)memory : null;
            _instructionMemory = memory as IInstructionMemory;
        }

        /// <summary>Читает слово по адресу команды; запрещает переход по нулевому адресу.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ulong MemFetch(ulong addr, bool rightHalf)
        {
            addr &= 0x7FFF;
            if (addr == 0 && !_allowZeroInstructionAddress)
                ThrowJumpToZero();
            return _coreMemory is { } core
                ? core.Read((uint)addr).Value : FetchOther((uint)addr, rightHalf);
        }

        private ulong FetchOther(uint address, bool rightHalf) => _instructionMemory is { } memory
            ? memory.FetchInstruction(address, rightHalf).Value : _memory.Read(address).Value;

        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowJumpToZero() => throw new ProcessorException("Jump to zero");

        /// <summary>Читает слово данных с проверкой load-watchpoint (mode=2).</summary>
        internal ulong MemLoad(uint addr)
        {
            addr &= 0x7FFF;
            if (_debugWatch.MemoryWatchArmed && _debugWatch.DebugCheckMemory(addr, 2))
                throw new Processor.DebugWatchAbortException();
            if (addr == 0)
                return 0;
            return _memory.Read(addr).Value;
        }

        /// <summary>Записывает слово данных с проверкой store-watchpoint (mode=1).</summary>
        internal void MemStore(uint addr, ulong val)
        {
            addr &= 0x7FFF;
            if (_debugWatch.MemoryWatchArmed && _debugWatch.DebugCheckMemory(addr, 1))
                throw new Processor.DebugWatchAbortException();
            if (addr == 0)
                return;
            _memory.Write(addr, new Word48(val));
        }
    }
}
