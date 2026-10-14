using Besm6.Architecture.Isa;
using Besm6.Processor.Cpu;

namespace Besm6.Runtime.Extracodes;
/// <summary>Counts calls without guest output; it neither traces nor performs host I/O.</summary>
internal sealed class HostedHangDetector
{
    private int _noOutputCount;
    private const int NoOutputLimit = 500;
    internal bool Enabled { get; set; }

    internal void Observe(Extracode code, uint k)
    {
        if (!Enabled)
            return;
        int opcode = (int)code;
        _noOutputCount++;
        if (code == Extracode.E64 || code == Extracode.E74)
        {
            _noOutputCount = 0; // got output or halt
        }
        else if (_noOutputCount > NoOutputLimit)
        {
            throw new ProcessorException($"Hang detected: MONSYS executing {NoOutputLimit}+ extracode calls without producing output. " + $"Last K=0{Convert.ToString(k, 8)}, opcode=0{Convert.ToString(opcode, 8)}. " + "This means MONSYS is in an I/O wait state expecting a compiler " + "(BEMSH/EXFOR/B) or resource that never completes. " + "This is a known limitation: the C++ reference (dubna/) also cannot " + "run ALGOL/FORTRAN/B jobs because the OS kernel is incomplete. " + "See ARCHITECTURE.md for execution limits and compatibility boundaries.");
        }
    }
}
