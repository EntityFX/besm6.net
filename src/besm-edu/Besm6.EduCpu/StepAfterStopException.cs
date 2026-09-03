namespace Besm6.EduCpu;

/// <summary>Попытка выполнить Step() после STOP.</summary>
public sealed class StepAfterStopException : CpuException
{
    public StepAfterStopException(ushort address, Half half)
        : base($"Незаконный Step() после STOP (позиция 0{Oct.Pad(address, 5)} {half}).")
    {
    }
}