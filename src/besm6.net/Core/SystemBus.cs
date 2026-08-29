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

        // ВАЖНО (соответствие C++-референсу, ref/machine.cpp mem_load/mem_store):
        // в БЭСМ-6 нет memory-mapped I/O — память является простым массивом, а ввод-вывод
        // выполняется экстракодами (E70/E71 и т.п.) через ту же основную память.
        // Раньше весь диапазон [_deviceStartAddr.._deviceEndAddr] маршрутизировался в
        // DeviceManager, где запись по незанятому адресу ТИХО СБРАСЫВАЛАСЬ, а чтение
        // возвращало 0. Это ломало ВСЮ память в этом диапазоне (например, a400:
        // mem[4646] должен был хранить 0x202020202020, но возвращал 0).
        // Теперь устройство используется ТОЛЬКО если по точному адресу зарегистрировано,
        // иначе — обычная основная память (как в C++).
        private bool IsDeviceAddress(uint address) =>
            address >= _deviceStartAddr && address <= _deviceEndAddr && _deviceManager.HasDevice(address);

        public Word48 Read(uint address)
        {
            if (IsDeviceAddress(address))
            {
                return _deviceManager.Read(address);
            }
            return _coreMemory.Read(address);
        }

        public void Write(uint address, Word48 value)
        {
            if (IsDeviceAddress(address))
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