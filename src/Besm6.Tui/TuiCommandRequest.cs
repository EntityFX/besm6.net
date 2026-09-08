namespace Besm6.Tui
{
    /// <summary>
    /// Результат разбора строки ввода TUI: команда, первичный аргумент
    /// и (для <see cref="TuiCommand.Write"/>) вторичный аргумент.
    /// Чистая модель без обращения к машине и файлам.
    /// </summary>
    public readonly record struct TuiCommandRequest(TuiCommand Command, string Argument, string Secondary)
    {
        /// <summary>Распознана ли команда.</summary>
        public bool IsKnown => Command != TuiCommand.Unknown;
    }
}
