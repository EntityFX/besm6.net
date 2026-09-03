namespace Besm6.EduCpu;

/// <summary>Достигнут лимит шагов до STOP.</summary>
public sealed class StepLimitExceededException : CpuException
{
    public StepLimitExceededException(int executed, int limit)
        : base($"Лимит шагов достигнут: выполнено {executed} из {limit}, STOP не выполнен.")
    {
    }
}