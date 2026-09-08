namespace Besm6.Tui
{
    /// <summary>
    /// Состояние TUI-сессии: RUN/HALT-флаги, счётчик инструкций, окно памяти
    /// и строка статуса. Хранится отдельно от рендера и контроллера, чтобы
    /// панель можно было выводить из чистого снимка.
    /// </summary>
    public sealed class TuiSessionState
    {
        /// <summary>Загруженный .dub-файл (или null, если файл не загружен).</summary>
        public string? JobFile { get; set; }

        /// <summary>Машина «запущена» (RUN-лампа).</summary>
        public bool Running { get; set; }

        /// <summary>Выполнена команда СТОП.</summary>
        public bool Halted { get; set; }

        /// <summary>Счётчик инструкций (за сессию).</summary>
        public long InstructionCount { get; set; }

        /// <summary>Базовый адрес окна памяти.</summary>
        public int MemoryBase { get; set; }

        /// <summary>Строка статуса/сообщения для панели.</summary>
        public string Status { get; set; } = "ready";

        /// <summary>Создана ли машина (есть ли память и регистры).</summary>
        public bool HasMachine { get; set; }
    }
}
