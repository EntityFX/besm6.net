using System;

namespace Besm6.Core
{
    /// <summary>
    /// Системная шина БЭСМ-6.
    /// Маршрутизирует запросы чтения/записи между основной памятью и периферийными устройствами.
    /// </summary>
    public class SystemBus : IMemory
    {
        private readonly IMemory _coreMemory;
        private readonly DeviceManager _deviceManager;
        private readonly int _deviceStartAddr;
        private readonly int _deviceEndAddr;

        public int Size => _coreMemory.Size;

        public SystemBus(IMemory coreMemory, DeviceManager deviceManager, int deviceStartAddr = 0x1000, int deviceEndAddr = 0x1FFF)
        {
            _coreMemory = coreMemory;
            _deviceManager = deviceManager;
            _deviceStartAddr = deviceStartAddr;
            _deviceEndAddr = deviceEndAddr;
        }

        public Word48 Read(int address)
        {
            if (address >= _deviceStartAddr && address <= _deviceEndAddr)
            {
                return _deviceManager.Read(address);
            }
            return _coreMemory.Read(address);
        }

        public void Write(int address, Word48 value)
        {
            if (address >= _deviceStartAddr && address <= _deviceEndAddr)
            {
                _deviceManager.Write(address, value);
            }
            else
            {
                _coreMemory.Write(address, value);
            }
        }
    }
}