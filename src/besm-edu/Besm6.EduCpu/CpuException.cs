namespace Besm6.EduCpu;

/// <summary>Базовый тип диагностических ошибок учебного процессора.</summary>
public class CpuException : Exception
{
    public CpuException(string message) : base(message)
    {
    }

    public CpuException(string message, Exception inner) : base(message, inner)
    {
    }
}