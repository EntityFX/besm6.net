using System;
using System.Collections.Generic;

namespace Besm6.Core
{
    /// <summary>
    /// Менеджер периферийных устройств БЭСМ-6.
    /// Управляет подключением устройств и маршрутизацией ввода-вывода.
    /// </summary>
    public class DeviceManager
    {
        private readonly Dictionary<uint, IDevice> _devices = new Dictionary<uint, IDevice>();

        /// <summary>
        /// Подключить устройство по определенному адресу.
        /// </summary>
        public void RegisterDevice(uint address, IDevice device)
        {
            _devices[address] = device;
        }

        /// <summary>
        /// Запись данных в устройство.
        /// </summary>
        public void Write(uint address, Word48 value)
        {
            if (_devices.TryGetValue(address, out var device))
            {
                device.Write(value);
            }
        }

        /// <summary>
        /// Чтение данных из устройства.
        /// </summary>
        public Word48 Read(uint address)
        {
            if (_devices.TryGetValue(address, out var device))
            {
                return device.Read();
            }
            return new Word48(0);
        }

        /// <summary>Зарегистрировано ли устройство по указанному адресу.</summary>
        public bool HasDevice(uint address) => _devices.ContainsKey(address);

        public IEnumerable<IDevice> GetDevices() => _devices.Values;

        public IDevice? GetDevice(uint address)
        {
            if (_devices.TryGetValue(address, out var device))
                return device;
            return null;
        }
    }
}