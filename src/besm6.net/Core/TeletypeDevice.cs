using System;
using System.Collections.Generic;
using System.Text;

namespace Besm6.Core
{
    /// <summary>
    /// Эмуляция телетайпа БЭСМ-6.
    /// Работает с символами (6-битная кодировка БЭСМ-6, упрощенно до ASCII).
    /// </summary>
    public class TeletypeDevice : IDevice
    {
        public string DeviceId { get; }
        public string DeviceName { get; }
        private readonly Queue<char> _inputBuffer = new Queue<char>();
        private readonly StringBuilder _outputBuffer = new StringBuilder();

        public TeletypeDevice(int id)
        {
            DeviceId = $"Teletype{id}";
            DeviceName = $"Teletype #{id}";
        }

        public void Initialize()
        {
            // Телетайп готов к работе сразу
        }

        public void ProcessCommand(byte command, Word48 parameter)
        {
            // Упрощенно: команда 0 - сброс вывода
            if (command == 0)
            {
                ClearOutput();
                Console.WriteLine($"[{DeviceName}] Output cleared.");
            }
        }

        public Word48 Read()
        {
            return ReadCharacter();
        }

        public void Write(Word48 value)
        {
            WriteCharacter(value);
        }

        /// <summary>
        /// Симуляция ввода символа с физического устройства.
        /// </summary>
        public void SendInput(string text)
        {
            foreach (var c in text)
            {
                _inputBuffer.Enqueue(c);
            }
        }

        /// <summary>
        /// Чтение одного символа (для DMA или CPU).
        /// </summary>
        public Word48 ReadCharacter()
        {
            if (_inputBuffer.Count == 0)
                return new Word48(0);

            char c = _inputBuffer.Dequeue();

            // В БЭСМ-6 символы упаковывались в слова. 
            // Упрощенно возвращаем код символа в Word48.
            return Word48.FromInt48((uint)c);
        }

        /// <summary>
        /// Запись одного символа на печать.
        /// </summary>
        public void WriteCharacter(Word48 word)
        {
            char c = (char)(uint)word.ToInt48();
            _outputBuffer.Append(c);
            
            // В реальном времени здесь была бы печать на бумаге
            Console.WriteLine($"[{DeviceName} PRINT]: {c}");
        }

        public string GetFullOutput() => _outputBuffer.ToString();
        public void ClearOutput() => _outputBuffer.Clear();
    }
}