using Besm6.Architecture.Debug;
using Besm6.Architecture.Machine;
using Besm6.Core;
using Besm6.Processor.Cpu;
using System;

namespace Besm6.Processor.Diagnostics
{
    /// <summary>
    /// Отладочные перехваты (watchpoints) процессора: fetch и memory watch.
    /// Вынесено из Processor.cs (Этап 4 рефакторинга). Работает через ссылку
    /// на владельца Processor для доступа к регистрам и трассировке.
    /// </summary>
    internal sealed class ProcessorDebugWatch
    {
        private readonly IProcessor _proc;
        private readonly IProcessorState _state;

        internal ProcessorDebugWatch(IProcessor proc, IProcessorState state)
        {
            _proc = proc;
            _state = state;
        }

        internal bool MemoryWatchArmed => _state.DebugState.DebugMemoryArmed;

        /// <summary>Сбрасывает состояние отладочных перехватов при Reset процессора.</summary>
        internal void Reset()
        {
            _state.DebugState.DebugFetchArmed = false;
            _state.DebugState.DebugMemoryArmed = false;
            _state.DebugState.DebugWatchSuppressed = false;
            _state.PreviousDebugAbort = 0;
        }

        /// <summary>Вооружает fetch- или memory-watchpoint по режиму mode (0=fetch, 1=store, 2=load).</summary>
        internal void ArmDebugWatch(uint xfer, bool printInfo, uint mode, uint watch, uint cont)
        {
            xfer &= 0x7FFF;
            watch &= 0x7FFF;
            cont &= 0x7FFF;
            if (xfer == 0)
                xfer = _state.PreviousDebugAbort != 0 ? _state.PreviousDebugAbort : cont;

            switch (mode)
            {
                case 0:
                    _state.DebugState.DebugFetchArmed = true;
                    _state.DebugState.DebugFetchAddress = watch;
                    _state.DebugState.DebugFetchContinuation = cont;
                    _state.DebugState.DebugFetchPrintInfo = printInfo;
                    break;
                case 1:
                case 2:
                    _state.DebugState.DebugMemoryArmed = true;
                    _state.DebugState.DebugMemoryAddress = watch;
                    _state.DebugState.DebugMemoryContinuation = cont;
                    _state.DebugState.DebugMemoryPrintInfo = printInfo;
                    _state.DebugState.DebugMemoryMode = mode;
                    break;
                default:
                    throw new ProcessorException("Bad debug watchpoint mode");
            }

            _state.K = xfer;
            _state.IsRightHalf = false;
        }

        /// <summary>Проверяет fetch-watchpoint перед исполнением инструкции. true — перехвачено.</summary>
        internal bool DebugCheckFetch(uint addr, uint opcode)
        {
            if (_state.DebugState.DebugWatchSuppressed || !_state.DebugState.DebugFetchArmed ||
                _state.DebugState.DebugFetchAddress != (addr & 0x7FFF))
                return false;

            uint cont = _state.DebugState.DebugFetchContinuation;
            bool printInfo = _state.DebugState.DebugFetchPrintInfo;
            _state.DebugState.DebugFetchArmed = false;
            DebugFire(cont, printInfo, opcode);
            return true;
        }

        /// <summary>Проверяет memory-watchpoint при чтении (mode=2) или записи (mode=1). true — перехвачено.</summary>
        internal bool DebugCheckMemory(uint addr, uint mode)
        {
            if (_state.DebugState.DebugWatchSuppressed || !_state.DebugState.DebugMemoryArmed ||
                _state.DebugState.DebugMemoryMode != mode || _state.DebugState.DebugMemoryAddress != (addr & 0x7FFF))
                return false;

            uint cont = _state.DebugState.DebugMemoryContinuation;
            bool printInfo = _state.DebugState.DebugMemoryPrintInfo;
            _state.DebugState.DebugMemoryArmed = false;
            DebugFire(cont, printInfo, 0);
            return true;
        }

        private void DebugFire(uint cont, bool printInfo, uint opcode)
        {
            _state.DebugState.DebugWatchSuppressed = true;
            try
            {
                if (printInfo)
                    _proc.TraceInstruction?.Invoke(_state.K, _state.IsRightHalf, _state.RawInstruction, opcode);

                _state.PreviousDebugAbort = cont;
                _state.K = cont & 0x7FFF;
                _state.IsRightHalf = false;
                _state.ApplyC = false;
                _state.C = 0;
            }
            finally
            {
                _state.DebugState.DebugWatchSuppressed = false;
            }
        }
    }
}
