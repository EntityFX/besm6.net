namespace Besm6.EduCpu;

/// <summary>Обращение к адресу за пределами 15-разрядного адресного пространства.</summary>
public sealed class OutOfRangeAddressException : CpuException
{
    public OutOfRangeAddressException(string message) : base(message)
    {
    }
}