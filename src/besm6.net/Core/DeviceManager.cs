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
        private readonly Dictionary<int, IDevice> _devices = new Dictionary<int, IDevice>();

        /// <summary>
        /// Подключить устройство по определенному адресу.
        /// </summary>
        public void RegisterDevice(int address, IDevice device)
        {
            _devices[address] = device;
        }

        /// <summary>
        /// Запись данных в устройство.
        /// </summary>
        public void Write(int address, Word48 value)
        {
            if (_devices.TryGetValue(address, out var device))
            {
                device.Write(value);
            }
        }

        /// <summary>
        /// Чтение данных из устройства.
        /// </summary>
        public Word48 Read(int address)
        {
            if (_devices.TryGetValue(address, out var device))
            {
                return device.Read();
            }
            return new Word48(0);
        }

        public IEnumerable<IDevice> GetDevices() => _devices.Values;

        public IDevice GetDevice(int address)
        {
            if (_devices.TryGetValue(address, out var device))
                return device;
            return null;
        }
    }
}