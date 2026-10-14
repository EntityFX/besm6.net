namespace Besm6.Architecture.Memory;

/// <summary>
/// Coherent loading and inspection of the host-visible word store. Reads do not
/// perform CPU control/protection checks or change CPU buffer recency. Writes
/// reconcile buffered values. Valid host addresses are below Size; mathematical
/// loading requires an explicit address-translating adapter.
/// </summary>
public interface IHostMemory : IMemory
{
}
