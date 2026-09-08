namespace Besm6.Assembler;

/// <summary>
/// Исключение, возникающее при ошибке ассемблирования: неизвестная мнемоника
/// или некорректный синтаксис строки в выбранном диалекте.
/// </summary>
public sealed class AssemblerException : Exception
{
    /// <summary>Создаёт исключение с сообщением.</summary>
    public AssemblerException(string message) : base(message)
    {
    }
}