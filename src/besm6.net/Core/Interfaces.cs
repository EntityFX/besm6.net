namespace Besm6.Core
{
    /// <summary>
    /// Интерфейс оперативной памяти БЭСМ-6.
    /// </summary>
    public interface IMemory
    {
        /// <summary>
        /// Считывает слово из памяти по указанному адресу.
        /// </summary>
        /// <param name="address">15-битный адрес ячейки памяти.</param>
        /// <returns>48-битное слово.</returns>
        Word48 Read(int address);

        /// <summary>
        /// Записывает слово в память по указанному адресу.
        /// </summary>
        /// <param name="address">15-битный адрес ячейки памяти.</param>
        /// <param name="word">Слово для записи.</param>
        void Write(int address, Word48 word);

        /// <summary>
        /// Общий размер доступной памяти в словах.
        /// </summary>
        int Size { get; }
    }

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