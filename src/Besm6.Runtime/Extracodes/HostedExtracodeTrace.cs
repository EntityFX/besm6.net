using Besm6.Architecture.Machine;

namespace Besm6.Runtime.Extracodes;
/// <summary>Extracode trace formatting and ownership of the lazily opened host writer.</summary>
internal sealed class HostedExtracodeTrace : IDisposable
{
    private readonly Func<TextWriter> _writerFactory;
    private TextWriter? _writer;
    internal bool Enabled { get; }

    internal HostedExtracodeTrace() : this(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BESM6_TRACE")), () => new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(), "ec_trace.log"), append: false) { AutoFlush = true })
    {
    }

    internal HostedExtracodeTrace(bool enabled, Func<TextWriter> writerFactory)
    {
        Enabled = enabled;
        _writerFactory = writerFactory;
    }

    internal void WriteLine(string line) => (_writer ??= _writerFactory()).WriteLine(line);
    internal void Observe(ExtracodeCall call, IReadOnlyRegisters registers)
    {
        if (!Enabled)
            return;
        long k = registers.K;
        int opcode = (int)call.Code;
        int reg = call.Register;
        uint rawAddr = call.RawAddress;
        bool rFlag = call.IsRightHalf;
        static string Oct(long v, int width) => Convert.ToString(v, 8).PadLeft(width, '0');
        // mnemonic = *NN [addr] [(reg)]  (см. besm6_print_instruction_mnemonics)
        string mnem = "*" + Convert.ToString(opcode, 8);
        if (rawAddr != 0)
        {
            mnem += " ";
            if (rawAddr >= 0x7FC0)
                mnem += "-" + Convert.ToString((rawAddr ^ 0x7FFF) + 1, 8);
            else
                mnem += Convert.ToString(rawAddr, 8);
        }

        if (reg != 0)
        {
            if (rawAddr == 0)
                mnem += " ";
            mnem += "(" + Convert.ToString(reg, 8) + ")";
        }

        // исполнительный адрес (см. print_executive_address): = M[reg], если reg != 0
        string execAddr = "";
        if (reg != 0)
        {
            long mreg = registers.M[reg & 15] & 0x7FFF;
            execAddr = " = " + Convert.ToString(mreg, 8);
        }

        WriteLine($"{Oct(k, 5)} {(rFlag ? 'R' : 'L')}: {Oct(reg, 2)} {Oct(opcode, 3)} {Oct(rawAddr, 4)} {mnem}{execAddr}");
    }

    public void Dispose() => _writer?.Dispose();
}
