using Besm6.Processor.Cpu;
using Besm6.Processor.Memory;
using Besm6.Runtime.Encoding;
using Besm6.Runtime.Storage;

namespace Besm6.Runtime.Extracodes
{
    internal sealed partial class HostedPrintService
    {
        // ─── E64: вывод текста (полный протокол, см. ExtracodeHandler.E64.cs) ───
        internal void E64(long aex)
        {
            int addr = (int)(_cpu.GetM(M16) & 0x7FFF);
            E64Full(addr);
        }
    }
}
