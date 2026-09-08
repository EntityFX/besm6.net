namespace Besm6.Runtime
{
    /// <summary>
    /// Результат загрузки/выполнения.
    /// </summary>
    public sealed class LoadResult
    {
        public bool Success { get; private init; }
        public bool Stopped { get; private init; }
        /// <summary>K — счётчик команд в момент завершения загрузки или выполнения.</summary>
        public long K { get; private init; }
        public long Instructions { get; private init; }
        public string? ErrorMessage { get; private init; }
        public bool LimitExceeded { get; private init; }

        public static LoadResult Halt(long k, long instr) => new()
        { Success = true, Stopped = true, K = k, Instructions = instr };
        public static LoadResult StoppedByLimit(long k, long instr) => new()
        { Success = false, Stopped = false, K = k, Instructions = instr, LimitExceeded = true };
        public static LoadResult Failed(string msg, long k, long instr) => new()
        { Success = false, Stopped = false, K = k, Instructions = instr, ErrorMessage = msg };

        public override string ToString()
        {
            if (Stopped) return $"Halted by STOP at 0{K:X} after {Instructions} instructions";
            if (LimitExceeded) return $"Instruction limit exceeded at 0{K:X} after {Instructions} instructions";
            return $"Error at 0{K:X}: {ErrorMessage}";
        }

        private LoadResult() { }
    }
}