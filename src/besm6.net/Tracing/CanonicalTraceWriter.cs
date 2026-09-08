using System.Text;
using Besm6.Core;

namespace Besm6.Tracing
{
    /// <summary>
    /// Записывает типизированную трассировку процессора в совместимый canonical TSV.
    /// </summary>
    public sealed class CanonicalTraceWriter : IDisposable
    {
        private readonly StreamWriter _writer;
        private readonly ulong _limit;
        private ulong _written;

        public CanonicalTraceWriter(string path, ulong limit = ulong.MaxValue)
        {
            _limit = limit;
            _writer = new StreamWriter(path, false, new UTF8Encoding(false));
            _writer.WriteLine(BuildHeader());
        }

        /// <summary>Записывает одну завершённую инструкцию.</summary>
        public void Write(InstructionTraceRecord record)
        {
            if (_written >= _limit)
                return;

            _written++;
            var line = new StringBuilder(512);
            line.Append(record.Sequence)
                .Append('\t').Append(record.Before.K)
                .Append('\t').Append(record.Before.IsRightHalf ? 'R' : 'L')
                .Append('\t').Append(record.RawWord.Value.ToString("X12"))
                .Append('\t').Append(record.RawInstruction.ToString("X6"))
                .Append('\t').Append((uint)record.Instruction.Opcode)
                .Append('\t').Append(record.Instruction.Register)
                .Append('\t').Append(record.Instruction.Address);
            AppendSnapshot(line, record.Before, includeControlPoint: false);
            AppendSnapshot(line, record.After, includeControlPoint: true);
            _writer.WriteLine(line.ToString());
        }

        /// <summary>Сбрасывает буфер на диск.</summary>
        public void Flush() => _writer.Flush();

        public void Dispose() => _writer.Dispose();

        private static string BuildHeader()
        {
            var header = new StringBuilder(512);
            header.Append("seq\tpc\thalf\traw48\trk24\topcode\treg\taddr")
                .Append("\ta_b\ty_b\tr_b\tc_b\tapply_c_b\taex_b\ticnt_b\tiadr_b");
            for (int index = 0; index < ArchitectureConstants.IndexRegCount; index++)
                header.Append("\tm").Append(index).Append("_b");
            header.Append("\ta_a\ty_a\tr_a\tc_a\tapply_c_a\taex_a\ticnt_a\tiadr_a\tpc_a\thalf_a");
            for (int index = 0; index < ArchitectureConstants.IndexRegCount; index++)
                header.Append("\tm").Append(index).Append("_a");
            return header.ToString();
        }

        private static void AppendSnapshot(
            StringBuilder line,
            ProcessorSnapshot snapshot,
            bool includeControlPoint)
        {
            line.Append('\t').Append(snapshot.A.Value.ToString("X12"))
                .Append('\t').Append(snapshot.Y.Value.ToString("X12"))
                .Append('\t').Append(snapshot.R)
                .Append('\t').Append(snapshot.C)
                .Append('\t').Append(snapshot.ApplyC ? 1 : 0)
                .Append('\t').Append(snapshot.EffectiveAddress)
                .Append('\t').Append(snapshot.InterceptCount)
                .Append('\t').Append(snapshot.InterceptAddress);
            if (includeControlPoint)
            {
                line.Append('\t').Append(snapshot.K)
                    .Append('\t').Append(snapshot.IsRightHalf ? 'R' : 'L');
            }
            foreach (uint register in snapshot.M)
                line.Append('\t').Append(register);
        }
    }
}
