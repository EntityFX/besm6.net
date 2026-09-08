namespace Besm6.Tui
{
    /// <summary>
    /// Разбор строки команды TUI. Понимает псевдонимы (q/exit, h, s/c/cont/continue,
    /// m, w, rst); никакого обращения к машине, консоли и файлам не выполняет.
    /// </summary>
    public static class TuiCommandParser
    {
        /// <summary>
        /// Разбирает строку ввода: «cmd [arg [value]]». Регистр не учитывается.
        /// Для <see cref="TuiCommand.Write"/> первичный аргумент — адрес, вторичный — значение.
        /// </summary>
        public static TuiCommandRequest Parse(string line)
        {
            line = line.Trim();
            if (line.Length == 0)
                return new TuiCommandRequest(TuiCommand.Unknown, string.Empty, string.Empty);

            int spaceIdx = line.IndexOf(' ');
            string cmd = (spaceIdx >= 0 ? line.Substring(0, spaceIdx) : line).ToLowerInvariant();
            string arg = spaceIdx >= 0 ? line.Substring(spaceIdx + 1).Trim() : string.Empty;

            switch (cmd)
            {
                case "quit": case "exit": case "q":
                    return new TuiCommandRequest(TuiCommand.Quit, string.Empty, string.Empty);
                case "help": case "h":
                    return new TuiCommandRequest(TuiCommand.Help, string.Empty, string.Empty);
                case "load":
                    return new TuiCommandRequest(TuiCommand.Load, arg, string.Empty);
                case "run":
                    return new TuiCommandRequest(TuiCommand.Run, string.Empty, string.Empty);
                case "step": case "cont": case "continue": case "c": case "s":
                    return new TuiCommandRequest(TuiCommand.Step, string.Empty, string.Empty);
                case "mem": case "m":
                    return new TuiCommandRequest(TuiCommand.Mem, arg, string.Empty);
                case "asm":
                    return new TuiCommandRequest(TuiCommand.Asm, arg, string.Empty);
                case "write": case "w":
                    var parts = arg.Split(' ', 2);
                    return new TuiCommandRequest(
                        TuiCommand.Write,
                        parts.Length > 0 ? parts[0].Trim() : string.Empty,
                        parts.Length > 1 ? parts[1].Trim() : string.Empty);
                case "reset": case "rst":
                    return new TuiCommandRequest(TuiCommand.Reset, string.Empty, string.Empty);
                default:
                    return new TuiCommandRequest(TuiCommand.Unknown, arg, string.Empty);
            }
        }
    }
}
