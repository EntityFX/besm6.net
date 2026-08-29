using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Besm6.Core;

namespace Besm6.Tests
{
    /// <summary>
    /// Регрессионный тест на маршрутизацию памяти SystemBus.
    ///
    /// Истории: в C++-референсе (ref/machine.cpp mem_load/mem_store) память — простой
    /// массив, memory-mapped I/O нет; ввод-вывод идёт экстракодами (E70/E71) через ту же
    /// основную память. Раньше SystemBus маршрутизировал ВСЁ из [0x1000..0x1FFF]
    /// (4096..8191) в DeviceManager, где запись по незанятому адресу тихо сбрасывалась,
    /// а чтение возвращало 0. Это ломало всю память в этом диапазоне: a400 на seq 430502
    /// читал mem[4646] и получал 0 вместо 0x202020202020 (который записан на seq 430492).
    ///
    /// Теперь устройство используется ТОЛЬКО по точному зарегистрированному адресу,
    /// остальные адреса — обычная основная память.
    /// </summary>
    [TestClass]
    public class SystemBusMemoryTests
    {
        // 4646 (0x1226) — адрес из a400, попадает в [4096, 8191], но устройства там нет.
        [TestMethod]
        public void UnregisteredDeviceRangeAddress_UsesCoreMemory()
        {
            var machine = new MachineCore();
            IMemory mem = machine.Memory;

            const uint addr = 4646; // в [4096, 8191], устройства не зарегистрировано
            mem.Write(addr, new Word48(0x202020202020UL));

            ulong read = mem.Read(addr).Value;
            Assert.AreEqual(0x202020202020UL, read,
                "Запись в [4096..8191] по незанятому адресу должна попасть в основную память, а не теряться.");
        }

        // Проверка нескольких точек диапазона, где устройств нет.
        [TestMethod]
        public void DeviceRange_WritesArePersistent()
        {
            var machine = new MachineCore();
            IMemory mem = machine.Memory;

            // 4097..8191 — устройств нет (устройства на 4096, 8192, 12288, 16384).
            uint[] addrs = { 4097, 4100, 4646, 5000, 8000, 8191 };
            for (int i = 0; i < addrs.Length; i++)
            {
                ulong v = 0xA00000000000UL + (ulong)i;
                mem.Write(addrs[i], new Word48(v));
            }
            for (int i = 0; i < addrs.Length; i++)
            {
                ulong expected = 0xA00000000000UL + (ulong)i;
                ulong actual = mem.Read(addrs[i]).Value;
                Assert.AreEqual(expected, actual, $"Адрес {addrs[i]}: значение должно сохраниться в основной памяти.");
            }
        }

    }
}
