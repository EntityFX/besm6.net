namespace Besm6.Core
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
