using System;
using System.IO;
using System.Text;

namespace Besm6.Core
{
    /// <summary>
    /// Эмуляция магнитного барабана БЭСМ-6.
    /// Поддерживает хранение данных в файле и блочный обмен.
    /// </summary>
    public class MagneticDrumDevice : IDevice
    {
        private readonly string _filePath;
        private byte[] _data = Array.Empty<byte>();
        private const int SectorSize = 32768; // 32K слов по умолчанию
        private int _currentAddress = 0;

        public string DeviceId => "MagneticDrum";
        public string DeviceName => "Magnetic Drum";

        public MagneticDrumDevice(string filePath)
        {
            _filePath = filePath;
            Initialize();
        }

        public void Initialize()
        {
            LoadDrum();
        }

        public void ProcessCommand(byte command, Word48 parameter)
        {
            // Команды управления барабаном (например, позиционирование головки)
            // Упрощенно: устанавливаем текущий адрес
            _currentAddress = (int)parameter.ToInt48() % SectorSize;
            Console.WriteLine($"Drum: Head moved to 0x{_currentAddress:X}");
        }

        public Word48 Read()
        {
            Word48 word = ReadWord(_currentAddress);
            _currentAddress = (_currentAddress + 1) % SectorSize;
            return word;
        }

        public void Write(Word48 value)
        {
            WriteWord(_currentAddress, value);
            _currentAddress = (_currentAddress + 1) % SectorSize;
        }

        private void LoadDrum()
        {
            if (File.Exists(_filePath))
            {
                _data = File.ReadAllBytes(_filePath);
            }
            else
            {
                // Создаем пустой барабан (32K слов по 8 байт)
                _data = new byte[SectorSize * 8];
                SaveDrum();
            }
        }

        public void SaveDrum()
        {
            File.WriteAllBytes(_filePath, _data);
        }

        public Word48 ReadWord(int address)
        {
            if (address < 0 || address >= SectorSize)
                throw new IndexOutOfRangeException($"Drum address 0x{address:X} out of range.");

            int offset = address * 8;
            ulong val = BitConverter.ToUInt64(_data, offset);
            return new Word48(val);
        }

        public void WriteWord(int address, Word48 word)
        {
            if (address < 0 || address >= SectorSize)
                throw new IndexOutOfRangeException($"Drum address 0x{address:X} out of range.");

            int offset = address * 8;
            byte[] bytes = BitConverter.GetBytes(word.Value);
            Array.Copy(bytes, 0, _data, offset, 8);
        }

        /// <summary>
        /// Блочное чтение данных с барабана.
        /// </summary>
        public void ReadBlock(int startAddress, Word48[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = ReadWord(startAddress + i);
            }
        }

        /// <summary>
        /// Блочная запись данных на барабан.
        /// </summary>
        public void WriteBlock(int startAddress, Word48[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                WriteWord(startAddress + i, buffer[i]);
            }
            SaveDrum();
        }
    }
}
