using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Besm6.Core;
using Besm6.Loader;

namespace Besm6.Tests
{
    [TestClass]
    public class DiagShiftTest
    {
        [TestMethod]
        public void DumpAtDivergence()
        {
            var machine = new MachineCore();
            var loader = new DubnaLoader(machine) { Verbose = false };
            loader.Output = s => { };
            string? path = FindFile("examples", "name.dub");
            if (path == null)
            {
                Assert.Inconclusive("name.dub not found");
                return;
            }
            int count = 0;
            string dump = string.Empty;
            machine.StepTrace = (pc, word) =>
            {
                if (count == 70754)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"pc={machine.Cpu.GetPc():X} acc={machine.Cpu.GetAcc().Value:X} rmr={machine.Cpu.GetRmr().Value:X} rau={machine.Cpu.GetRau():X}");
                    for (int a = 0x698; a <= 0x6A0; a++)
                        sb.AppendLine($"mem[{a:X3}] = {machine.Memory.Read((uint)a).Value:X12}");
                    for (int r = 0; r < 16; r++)
                        sb.AppendLine($"M[{r}] = {machine.Cpu.GetM(r):X}");
                    dump = sb.ToString();
                    throw new Exception("dumpdone");
                }
                count++;
            };
            try { loader.RunScript(path); }
            catch (Exception ex) { if (ex.Message != "dumpdone") throw; }
            string repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, ".."));
            string outputDirectory = Path.Combine(repositoryRoot, "tests-run");
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "namedub_main_dump.txt"), dump ?? string.Empty);
            Assert.Inconclusive("dump saved");
        }

        private static string? FindFile(string relativePath, string fileName)
        {
            string? dir = Directory.GetCurrentDirectory();
            while (dir != null)
            {
                string p = Path.Combine(dir, relativePath, fileName);
                if (File.Exists(p)) return p;
                dir = Directory.GetParent(dir)?.FullName;
            }
            return null;
        }
    }
}
