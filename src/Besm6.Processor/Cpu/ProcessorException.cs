using System;

namespace Besm6.Core
{
    /// <summary>
    /// Исключение исполнения процессора БЭСМ-6 (деление на ноль, неопределённые
    /// экстракоды и т.п.).
    /// </summary>
    public class ProcessorException : Exception
    {
        public ProcessorException(string message) : base(message) { }
    }
}
