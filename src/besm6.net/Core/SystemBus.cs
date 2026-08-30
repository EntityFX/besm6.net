using System;

namespace Besm6.Core
{
    /// <summary>
    /// Системная шина БЭСМ-6.
    ///
    /// ВАЖНО (соответствие C++-референсу, ref/machine.cpp mem_load/mem_store):
    /// в БЭСМ-6 нет memory-mapped I/O — память является простым массивом 32K слов,
    /// а ввод-вывод выполняется экстракодами (E70/E71 и т.п.) через ту же основную
    /// память. Поэтому шина НЕ маршрутизирует ни один адрес в периферийные
    /// устройства: ВСЕ обращения читают/пишут основную память.
    ///
    /// Истории (регрессии):
    /// 1) Раньше весь диапазон [0x1000..0x1FFF] (4096..8191) маршрутизировался в
    ///    DeviceManager, где запись по незанятому адресу тихо сбрасывалась, а чтение
    ///    возвращало 0. Это ломало всю память в этом диапазоне (a400: mem[4646]).
    /// 2) После частичной починки (только точные адреса устройств) адрес
    ///    0x1000 = 0o10000 = 4096 всё ещё перехватывался ConsoleDevice:
    ///    a400 (seq ~2.79M, xta 1(14)) читал mem[0o10000] и получал 0, тогда как
    ///    в C++ там 7777 0000 7770 0000 (записано циклом заполнения 34605-34607).
    ///    Теперь и точные адреса устройств не перехватываются: память плоская, как в C++.
    /// </summary>
    public class SystemBus : IMemory
    {
        private readonly IMemory _coreMemory;

        public int Size => _coreMemory.Size;

        public SystemBus(IMemory coreMemory, DeviceManager? deviceManager = null)
        {
            // deviceManager принимается ради совместимости фабрик, но для
            // маршрутизации памяти НЕ используется (см. комментарий выше).
            _coreMemory = coreMemory;
        }

        public Word48 Read(uint address)
        {
            return _coreMemory.Read(address);
        }

        public void Write(uint address, Word48 value)
        {
            _coreMemory.Write(address, value);
        }
    }
}
