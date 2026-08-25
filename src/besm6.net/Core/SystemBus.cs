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
        private readonly uint _deviceStartAddr;
        private readonly uint _deviceEndAddr;

        public int Size => _coreMemory.Size;

        public SystemBus(IMemory coreMemory, DeviceManager deviceManager, uint deviceStartAddr = 0x1000, uint deviceEndAddr = 0x1FFF)
        {
            _coreMemory = coreMemory;
            _deviceManager = deviceManager;
            _deviceStartAddr = deviceStartAddr;
            _deviceEndAddr = deviceEndAddr;
        }

        public Word48 Read(uint address)
        {
            if (address >= _deviceStartAddr && address <= _deviceEndAddr)
            {
                return _deviceManager.Read(address);
            }
            return _coreMemory.Read(address);
        }

        public void Write(uint address, Word48 value)
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