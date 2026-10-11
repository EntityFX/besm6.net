namespace Besm6.Runtime;

public sealed partial class SupervisorIoController
{
    private void TransferMedia(bool toMemory, MemoryWord50[] media, int serviceOffset, int serviceCount,
        uint serviceAddress, int dataOffset, int dataCount, uint dataAddress, Action completed, Action aborted)
    {
        if (_hardwareDma is { } dma)
        {
            var words = new SupervisorDmaWord[checked(serviceCount + dataCount)];
            for (int i = 0; i < serviceCount; i++) words[i] = new(checked(serviceAddress + (uint)i), serviceOffset + i);
            for (int i = 0; i < dataCount; i++) words[serviceCount + i] = new(checked(dataAddress + (uint)i), dataOffset + i);
            try { dma.Transfer(toMemory, media, words, completed, aborted); }
            catch { aborted(); throw; }
            return;
        }
        if (toMemory)
        {
            for (int i = 0; i < serviceCount; i++) _memory.PhysicalMemory.WriteRaw(serviceAddress + (uint)i, media[serviceOffset + i]);
            for (int i = 0; i < dataCount; i++) _memory.PhysicalMemory.WriteRaw(dataAddress + (uint)i, media[dataOffset + i]);
        }
        else
        {
            for (int i = 0; i < serviceCount; i++) media[serviceOffset + i] = _memory.PhysicalMemory.ReadRaw(serviceAddress + (uint)i);
            for (int i = 0; i < dataCount; i++) media[dataOffset + i] = _memory.PhysicalMemory.ReadRaw(dataAddress + (uint)i);
        }
        completed();
    }
}
