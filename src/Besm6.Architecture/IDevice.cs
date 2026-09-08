using Besm6.Architecture;

namespace Besm6.Core
{
    /// <summary>
    /// Базовый интерфейс для периферийных устройств.
    /// </summary>
    public interface IDevice
    {
        /// <summary>
        /// Уникальный идентификатор устройства.
        /// </summary>
        string DeviceId { get; }

        /// <summary>
        /// Инициализация устройства.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Обработка команды управления устройством.
        /// </summary>
        void ProcessCommand(byte command, Word48 parameter);

        /// <summary>
        /// Чтение данных из устройства.
        /// </summary>
        Word48 Read();

        /// <summary>
        /// Запись данных в устройство.
        /// </summary>
        void Write(Word48 value);
    }
}