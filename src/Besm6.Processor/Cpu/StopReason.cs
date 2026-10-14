namespace Besm6.Processor.Cpu
{
    /// <summary>
    /// Причина остановки процессора или ограниченного цикла исполнения.
    /// </summary>
    public enum StopReason
    {
        None,
        Stop,
        LimitExceeded,
        Error,
        Exception,
    }
}
