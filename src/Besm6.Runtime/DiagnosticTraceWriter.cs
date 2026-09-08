using System.Text;
namespace Besm6.Runtime
{
    /// <summary>
    /// Записывает человекочитаемую диагностическую трассировку инструкций.
    /// </summary>
    public sealed class DiagnosticTraceWriter : IDisposable
    {
        private readonly StreamWriter _writer;

        public DiagnosticTraceWriter(string path)
        {
            _writer = new StreamWriter(path, append: false, new UTF8Encoding(false))
            {
                AutoFlush = true,
            };
        }

        /// <summary>Записывает одну завершённую инструкцию.</summary>
        public void Write(InstructionTraceRecord record)
        {
            ProcessorSnapshot before = record.Before;
            uint address = record.Instruction.Address;
            if (before.ApplyC)
                address = ArchitectureConstants.NormalizeAddress(address + before.C);

            _writer.WriteLine(
                $"{before.K:X5} R={(before.IsRightHalf ? "R" : "L")} " +
                $"op={(uint)record.Instruction.Opcode,3} reg={record.Instruction.Register,2} addr={address,5} " +
                $"a={before.A.Value:X12} r={before.R:X1} c={before.C,5} m14={before.M[14],5} " +
                record.Instruction.Opcode);
        }

        /// <summary>Записывает совместимую строку изменения регистра.</summary>
        public void Write(RegisterTraceRecord record)
        {
            _writer.WriteLine($"{record.Name}\t{record.Value}");
        }

        public void Dispose() => _writer.Dispose();
    }
}
