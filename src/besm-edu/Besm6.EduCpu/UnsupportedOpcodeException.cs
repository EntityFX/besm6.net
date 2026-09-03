namespace Besm6.EduCpu;

/// <summary>Код операции вне учебного набора.</summary>
public sealed class UnsupportedOpcodeException : CpuException
{
    public UnsupportedOpcodeException(string message) : base(message)
    {
    }
}