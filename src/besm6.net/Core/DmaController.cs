using System;

namespace Besm6.Core
{
    /// <summary>
    /// Контроллер прямого доступа к памяти (DMA) для БЭСМ-6.
    /// Позволяет выполнять блочные передачи данных между устройствами и основной памятью.
    /// </summary>
    public class DmaController
    {
        private readonly IMemory _memory;
        private readonly DeviceManager _deviceManager;

        public DmaController(IMemory memory, DeviceManager deviceManager)
        {
            _memory = memory;
            _deviceManager = deviceManager;
        }

        /// <summary>
        /// Перенос блока данных из памяти в устройство (ВОП - Вывод Опорных Полей/Памяти).
        /// </summary>
        public void TransferMemoryToDevice(int deviceAddr, int startAddr, int count)
        {
            var device = _deviceManager.GetDevice(deviceAddr);
            if (device == null) return;

            if (device is MagneticDrumDevice drum)
            {
                Word48[] buffer = new Word48[count];
                for (int i = 0; i < count; i++) buffer[i] = _memory.Read(startAddr + i);
                drum.WriteBlock(0, buffer);
                Console.WriteLine($"DMA Block Write: Mem -> Drum | Count: {count}");
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    Word48 value = _memory.Read(startAddr + i);
                    _deviceManager.Write(deviceAddr, value);
                }
                Console.WriteLine($"DMA Word Write: Mem -> Device | Count: {count}");
            }
        }

        /// <summary>
        /// Перенос блока данных из устройства в память (ВОГ - Ввод Опорных Полей/Памяти).
        /// </summary>
        public void TransferDeviceToMemory(int deviceAddr, int startAddr, int count)
        {
            var device = _deviceManager.GetDevice(deviceAddr);
            if (device == null) return;

            if (device is MagneticDrumDevice drum)
            {
                Word48[] buffer = new Word48[count];
                drum.ReadBlock(0, buffer);
                for (int i = 0; i < count; i++) _memory.Write(startAddr + i, buffer[i]);
                Console.WriteLine($"DMA Block Read: Drum -> Mem | Count: {count}");
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    Word48 value = _deviceManager.Read(deviceAddr);
                    _memory.Write(startAddr + i, value);
                }
                Console.WriteLine($"DMA Word Read: Device -> Mem | Count: {count}");
            }
        }
    }
}