namespace Besm6.Processor.Diagnostics
{
    internal sealed class DebugState
    {
        public bool DebugFetchArmed { get; set; }
        public uint DebugFetchAddress { get; set; }
        public uint DebugFetchContinuation { get; set; }
        public bool DebugFetchPrintInfo { get; set; }
        public bool DebugMemoryArmed { get; set; }
        public uint DebugMemoryAddress { get; set; }
        public uint DebugMemoryContinuation { get; set; }
        public bool DebugMemoryPrintInfo { get; set; }
        public uint DebugMemoryMode { get; set; }
        public bool DebugWatchSuppressed { get; set; }
    }
}
