using System;
using System.IO;
using System.Text;

namespace Besm6.Core
{
    /// <summary>
    /// Эмуляция дискового устройства для БЭСМ-6.
    /// Работает с файлом на диске хоста как с образом диска БЭСМ-6.
    /// </summary>
    public class DiskDevice : IDevice
    {
        public string DeviceId => "Disk";
        public void Initialize() { }
        public void ProcessCommand(byte command, Word48 parameter) { }

        private readonly string _filePath;
        private long _currentPosition;
        private byte[] _diskImage;

        public DiskDevice(string filePath)
        {
            _filePath = filePath;
            _currentPosition = 0;
            LoadImage();
        }

        private void LoadImage()
        {
            if (File.Exists(_filePath))
            {
                _diskImage = File.ReadAllBytes(_filePath);
            }
            else
            {
                // Создаем пустой образ на 64КБ
                _diskImage = new byte[65536];
                File.WriteAllBytes(_filePath, _diskImage);
            }
        }

        public void Write(Word48 value)
        {
            // Запись слова БЭСМ-6 (48 бит -> 8 байт в файле для выравнивания)
            byte[] bytes = BitConverter.GetBytes(value.Value);
            for (int i = 0; i < 8 && (_currentPosition + i) < _diskImage.Length; i++)
            {
                _diskImage[(int)(_currentPosition + i)] = bytes[i];
            }
            _currentPosition += 8;
            SaveImage();
        }

        public Word48 Read()
        {
            if (_currentPosition + 8 > _diskImage.Length)
            {
                _currentPosition = 0; // Зацикливаем для простоты
            }

            byte[] buffer = new byte[8];
            Array.Copy(_diskImage, _currentPosition, buffer, 0, 8);
            long val = BitConverter.ToInt64(buffer, 0);
            _currentPosition += 8;

            return new Word48(val);
        }

        private void SaveImage()
        {
            File.WriteAllBytes(_filePath, _diskImage);
        }

        public void Seek(long position)
        {
            _currentPosition = position;
        }
    }
}