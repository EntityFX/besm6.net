using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Besm6.Core;

namespace Besm6.Tests
{
    /// <summary>
    /// Регрессионный тест на маршрутизацию памяти SystemBus.
    ///
    /// массив, memory-mapped I/O нет; ввод-вывод идёт экстракодами (E70/E71) через ту же
    /// основную память. Раньше SystemBus маршрутизировал ВСЁ из [0x1000..0x1FFF]
    /// (4096..8191) в DeviceManager, где запись по незанятому адресу тихо сбрасывалась,
    /// а чтение возвращало 0. Это ломало всю память в этом диапазоне: a400 на seq 430502
    /// читал mem[4646] и получал 0 вместо 0x202020202020 (который записан на seq 430492).
    ///
    /// Вторая регрессия: после частичной починки (только точные адреса устройств)
    /// адрес 0x1000 = 4096 = 0o10000 всё ещё перехватывался ConsoleDevice:
    /// там 7777 0000 7770 0000 (записано циклом заполнения на PC 34605-34607).
    ///
    /// Теперь устройство не перехватывает НИ ОДИН адрес: ВСЕ обращения попадают в
    /// </summary>
    [TestClass]
    public class SystemBusMemoryTests
    {
        // 4646 (0x1226) — адрес из a400, попадает в [4096, 8191].
        [TestMethod]
        public void UnregisteredDeviceRangeAddress_UsesCoreMemory()
        {
            var machine = new MachineCore();
            IMemory mem = machine.Memory;

            const uint addr = 4646; // в [4096, 8191]
            mem.Write(addr, new Word48(0x202020202020UL));

            ulong read = mem.Read(addr).Value;
            Assert.AreEqual(0x202020202020UL, read,
                "Запись в [4096..8191] должна попасть в основную память, а не теряться.");
        }

        // 4096 (0x1000) — адрес из a400, раньше перехватывался ConsoleDevice.
        [TestMethod]
        public void FormerConsoleDeviceAddress_UsesCoreMemory()
        {
            var machine = new MachineCore();
            IMemory mem = machine.Memory;

            // 0o10000 = 0x1000 = 4096 — адрес из a400; значение 0o777700007770000
            // (= 0xFFFF0000FFF0, 48 бит) — слово цикла заполнения 34605-34607.
            const uint addr = 4096;
            mem.Write(addr, new Word48(0xFFFF0000FFF0UL));

            ulong read = mem.Read(addr).Value;
            Assert.AreEqual(0xFFFF0000FFF0UL, read,
                "Адрес 0x1000 (0o10000) должен быть обычной основной памятью, как в C++-референсе.");
        }

        // Проверка нескольких точек диапазона, включая раньше занятые устройствами адреса.
        [TestMethod]
        public void DeviceRange_WritesArePersistent()
        {
            var machine = new MachineCore();
            IMemory mem = machine.Memory;

            uint[] addrs = { 4096, 4097, 4100, 4646, 5000, 8000, 8191 };
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
