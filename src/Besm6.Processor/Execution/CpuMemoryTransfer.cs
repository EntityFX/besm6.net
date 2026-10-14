using Besm6.Architecture.Numerics;

namespace Besm6.Processor.Execution;

/// <summary>A memory transfer requested by a prepared command.</summary>
internal readonly record struct CpuMemoryTransfer(uint Address, bool Write, Word48 Word);
