namespace Besm6.Cli
{
    /// <summary>
    /// Команда CLI симулятора БЭСМ-6.
    /// </summary>
    public interface ICommand
    {
        /// <summary>Имя команды (для использования).</summary>
        string Name { get; }

        /// <summary>Описание (для help).</summary>
        string Description { get; }

        /// <summary>Использование (для help).</summary>
        string Usage { get; }

        /// <summary>
        /// Выполнить команду.
        /// </summary>
        int Execute(string[] args);
    }
}