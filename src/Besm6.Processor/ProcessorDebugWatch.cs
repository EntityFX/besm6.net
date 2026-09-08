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

        private bool _fetchArmed;
        private uint _fetchAddr;
        private uint _fetchCont;
        private bool _fetchPrintInfo;
        private bool _memoryArmed;
        private uint _memoryAddr;
        private uint _memoryCont;
        private bool _memoryPrintInfo;
        private uint _memoryMode;
        private bool _suppressed;
        private uint _prevAbort;

        internal ProcessorDebugWatch(Processor proc)
        {
            _proc = proc;
        }

        /// <summary>Сбрасывает состояние отладочных перехватов при Reset процессора.</summary>
        internal void Reset()
        {
            _fetchArmed = false;
            _memoryArmed = false;
            _suppressed = false;
            _prevAbort = 0;
        }

        /// <summary>Вооружает fetch- или memory-watchpoint по режиму mode (0=fetch, 1=store, 2=load).</summary>
        internal void ArmDebugWatch(uint xfer, bool printInfo, uint mode, uint watch, uint cont)
        {
            xfer &= 0x7FFF;
            watch &= 0x7FFF;
            cont &= 0x7FFF;
            if (xfer == 0)
                xfer = _prevAbort != 0 ? _prevAbort : cont;

            switch (mode)
            {
                case 0:
                    _fetchArmed = true;
                    _fetchAddr = watch;
                    _fetchCont = cont;
                    _fetchPrintInfo = printInfo;
                    break;
                case 1:
                case 2:
                    _memoryArmed = true;
                    _memoryAddr = watch;
                    _memoryCont = cont;
                    _memoryPrintInfo = printInfo;
                    _memoryMode = mode;
                    break;
                default:
                    throw new ProcessorException("Bad debug watchpoint mode");
            }

            _proc._k = xfer;
            _proc._rightInstrFlag = false;
        }

        /// <summary>Проверяет fetch-watchpoint перед исполнением инструкции. true — перехвачено.</summary>
        internal bool DebugCheckFetch(uint addr, uint opcode)
        {
            if (_suppressed || !_fetchArmed || _fetchAddr != (addr & 0x7FFF))
                return false;

            uint cont = _fetchCont;
            bool printInfo = _fetchPrintInfo;
            _fetchArmed = false;
            DebugFire(cont, printInfo, opcode);
            return true;
        }

        /// <summary>Проверяет memory-watchpoint при чтении (mode=2) или записи (mode=1). true — перехвачено.</summary>
        internal bool DebugCheckMemory(uint addr, uint mode)
        {
            if (_suppressed || !_memoryArmed ||
                _memoryMode != mode || _memoryAddr != (addr & 0x7FFF))
                return false;

            uint cont = _memoryCont;
            bool printInfo = _memoryPrintInfo;
            _memoryArmed = false;
            DebugFire(cont, printInfo, 0);
            return true;
        }

        private void DebugFire(uint cont, bool printInfo, uint opcode)
        {
            _suppressed = true;
            try
            {
                if (printInfo)
                    _proc.TraceInstruction?.Invoke(_proc._k, _proc._rightInstrFlag, _proc._rk, opcode);

                _prevAbort = cont;
                _proc._k = cont & 0x7FFF;
                _proc._rightInstrFlag = false;
                _proc._applyC = false;
                _proc._c = 0;
            }
            finally
            {
                _suppressed = false;
            }
        }
    }
}