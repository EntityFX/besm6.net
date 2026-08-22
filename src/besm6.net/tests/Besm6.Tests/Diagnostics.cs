using Besm6.Core;
using Besm6.Loader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    [TestClass]
    public class Diagnostics
    {
        [TestMethod]
        public void TraceAlgol_Last30Instructions()
        {
            // Walk up to find the repo root that has BOTH tapes/ and examples/
            string repoRoot = System.IO.Directory.GetCurrentDirectory();
            while (repoRoot != null)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(repoRoot, "tapes"))
                    && System.IO.Directory.Exists(System.IO.Path.Combine(repoRoot, "examples")))
                    break;
                repoRoot = System.IO.Directory.GetParent(repoRoot)?.FullName;
            }
            if (repoRoot == null)
                Assert.Inconclusive("Cannot find repo root with tapes/ and examples/ directories");
            
            string tapesDir = System.IO.Path.Combine(repoRoot, "tapes");
            string dubPath = System.IO.Path.Combine(repoRoot, "examples", "algol.dub");

            var machine = new MachineCore();
            var loader = new DubnaLoader(machine, tapesDir)
            {
                InstructionLimit = 500,
                Verbose = true
            };

            // Capture all instructions.
            var trace = new System.Collections.Generic.List<(int pc, long word)>();
            loader.InstructionTrace = (pc, word) => trace.Add((pc, word));

            var job = JobParser.ParseFile(dubPath);
            var lines = System.IO.File.ReadAllLines(dubPath);
            var result = loader.RunJob(job, lines);

            // Write ALL instructions to a file for analysis.
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== TRACE: {trace.Count} instructions, result={result} ===");
            sb.AppendLine();
            for (int i = 0; i < trace.Count; i++)
            {
                var (pc, word) = trace[i];
                int opcode = (int)((word >> 42) & 0x3F);
                int addr = (int)((word >> 24) & 0x3FFFF);
                string octalPc = Convert.ToString(pc, 8).PadLeft(5, '0');
                string octalAddr = Convert.ToString(addr, 8).PadLeft(6, '0');
                sb.AppendLine($"[{i,4}] PC={octalPc} op={Convert.ToString(opcode, 8).PadLeft(2,'0')} addr={octalAddr} word=0x{word:X12}");
            }
            string outFile = System.IO.Path.Combine(repoRoot!, "diagnostics-output.txt");
            System.IO.File.WriteAllText(outFile, sb.ToString());
            Assert.Inconclusive("See diagnostics-output.txt");
        }
    }
}