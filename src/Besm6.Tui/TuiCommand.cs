namespace Besm6.Tui
{
    /// <summary>Команды TUI-панели.</summary>
    public enum TuiCommand
    {
        /// <summary>Команда не распознана.</summary>
        Unknown,
        /// <summary>Завершение работы.</summary>
        Quit,
        /// <summary>Справка.</summary>
        Help,
        /// <summary>Загрузка .dub-файла.</summary>
        Load,
        /// <summary>Пуск машины.</summary>
        Run,
        /// <summary>Один шаг процессора.</summary>
        Step,
        /// <summary>Окно памяти.</summary>
        Mem,
        /// <summary>Ассемблирование одной инструкции.</summary>
        Asm,
        /// <summary>Запись слова в память.</summary>
        Write,
        /// <summary>Сброс процессора.</summary>
        Reset
    }
}
