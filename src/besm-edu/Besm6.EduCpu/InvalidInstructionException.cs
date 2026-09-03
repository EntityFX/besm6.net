namespace Besm6.EduCpu;

/// <summary>Ошибка кодирования/декодирования (широта полей, состав полей).</summary>
public sealed class InvalidInstructionException : CpuException
{
    public InvalidInstructionException(string message) : base(message)
    {
    }
}