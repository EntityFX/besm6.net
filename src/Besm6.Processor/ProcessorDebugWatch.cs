using System;

namespace Besm6.Core
{
    /// <summary>
    /// Отладочные перехваты (watchpoints) процессора: fetch и memory watch.
    /// Вынесено из Processor.cs (Этап 4 рефакторинга). Работает через ссылку
    /// на владельца Processor для доступа к регистрам и трассировке.
    /// </summary>
    internal sealed class ProcessorDebugWatch
    {
        private readonly Processor _proc;
        private readonly ProcessorState _state;

        internal ProcessorDebugWatch(Processor proc, ProcessorState state)
        {
            _proc = proc;
            _state = state;
        }

        /// <summary>Сбрасывает состояние отладочных перехватов при Reset процессора.</summary>
        internal void Reset()
        {
            _state.DebugFetchArmed = false;
            _state.DebugMemoryArmed = false;
            _state.DebugWatchSuppressed = false;
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
                    _state.DebugFetchArmed = true;
                    _state.DebugFetchAddress = watch;
                    _state.DebugFetchContinuation = cont;
                    _state.DebugFetchPrintInfo = printInfo;
                    break;
                case 1:
                case 2:
                    _state.DebugMemoryArmed = true;
                    _state.DebugMemoryAddress = watch;
                    _state.DebugMemoryContinuation = cont;
                    _state.DebugMemoryPrintInfo = printInfo;
                    _state.DebugMemoryMode = mode;
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
            if (_state.DebugWatchSuppressed || !_state.DebugFetchArmed ||
                _state.DebugFetchAddress != (addr & 0x7FFF))
                return false;

            uint cont = _state.DebugFetchContinuation;
            bool printInfo = _state.DebugFetchPrintInfo;
            _state.DebugFetchArmed = false;
            DebugFire(cont, printInfo, opcode);
            return true;
        }

        /// <summary>Проверяет memory-watchpoint при чтении (mode=2) или записи (mode=1). true — перехвачено.</summary>
        internal bool DebugCheckMemory(uint addr, uint mode)
        {
            if (_state.DebugWatchSuppressed || !_state.DebugMemoryArmed ||
                _state.DebugMemoryMode != mode || _state.DebugMemoryAddress != (addr & 0x7FFF))
                return false;

            uint cont = _state.DebugMemoryContinuation;
            bool printInfo = _state.DebugMemoryPrintInfo;
            _state.DebugMemoryArmed = false;
            DebugFire(cont, printInfo, 0);
            return true;
        }

        private void DebugFire(uint cont, bool printInfo, uint opcode)
        {
            _state.DebugWatchSuppressed = true;
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
                _state.DebugWatchSuppressed = false;
            }
        }
    }
}
