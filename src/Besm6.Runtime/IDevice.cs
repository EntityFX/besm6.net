namespace Besm6.Runtime
{
    /// <summary>
    /// Базовый интерфейс периферийного устройства БЭСМ-6.
    /// </summary>
    public interface IDevice
    {
        /// <summary>Уникальный идентификатор устройства.</summary>
        string DeviceId { get; }

        /// <summary>Инициализирует устройство.</summary>
        void Initialize();

        /// <summary>Обрабатывает команду управления устройством.</summary>
        void ProcessCommand(byte command, Word48 parameter);

        /// <summary>Считывает слово из устройства.</summary>
        Word48 Read();

        /// <summary>Записывает слово в устройство.</summary>
        void Write(Word48 value);
    }
}
