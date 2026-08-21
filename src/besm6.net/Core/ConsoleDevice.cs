using System;
using System.Text;

namespace Besm6.Core
{
    /// <summary>
    /// Эмуляция консольного устройства ввода-вывода для БЭСМ-6.
    /// </summary>
    public class ConsoleDevice : IDevice
    {
        public string DeviceId => "Console";
        public void Initialize() { }
        public void ProcessCommand(byte command, Word48 parameter) { }

        private readonly StringBuilder _outputBuffer = new StringBuilder();
        private string _inputQueue = "";

        public void Write(Word48 value)
        {
            // В БЭСМ-6 вывод часто шел посимвольно или строками.
            // Здесь мы интерпретируем значение Word48 как ASCII символ (младший байт).
            char c = (char)(value.Value & 0xFF);
            _outputBuffer.Append(c);
            
            // Сразу выводим в консоль хоста для наглядности
            Console.Write(c);
        }

        public Word48 Read()
        {
            if (string.IsNullOrEmpty(_inputQueue))
            {
                // В реальном времени мы бы ждали ввода, 
                // но для симулятора просто вернем 0 или попробуем прочитать из Console.
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    _inputQueue = key.KeyChar.ToString();
                }
                else
                {
                    return new Word48(0);
                }
            }

            char c = _inputQueue[0];
            _inputQueue = _inputQueue.Substring(1);
            return new Word48((int)c);
        }

        public void Clear()
        {
            _outputBuffer.Clear();
            _inputQueue = "";
        }
    }
}