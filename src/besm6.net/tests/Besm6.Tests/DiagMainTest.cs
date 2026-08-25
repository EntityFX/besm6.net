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
        private sealed class Mem : IMemory
        {
            private readonly Word48[] _w = new Word48[32768];
            public Word48 Read(uint address) => _w[address & 0x7FFF];
            public void Write(uint address, Word48 word) => _w[address & 0x7FFF] = word;
            public int Size => 32768;
        }

        [TestMethod]
        public void Shift9()
        {
            var cpu = new Processor(new Mem());
            cpu.SetAcc(0x201);
            cpu.ArithShift(9);
            throw new Exception($"acc=0x{cpu.GetAcc().Value:X} rmr=0x{cpu.GetRmr().Value:X}");
        }

        [TestMethod]
        public void TraceNameDub()
        {
            var machine = new MachineCore();
            var pcs = new System.Text.StringBuilder();
            var states = new System.Text.StringBuilder();
            int count = 0;
            machine.StepTrace = (pc, word) =>
            {
                if (count >= 69000 && count < 73000)
                    states.Append(count).Append(":pc=").Append(pc).Append(" acc=").Append(machine.Cpu.GetAcc().Value.ToString("X")).Append(" rmr=").Append(machine.Cpu.GetRmr().Value.ToString("X")).Append(" rau=").Append(machine.Cpu.GetRau().ToString("X")).Append("\n");
                count++;
            };
            var loader = new DubnaLoader(machine) { Verbose = false };
            loader.Output = s => { };
            string path = FindFile("examples", "name.dub");
            if (path == null) Assert.Inconclusive("name.dub not found");
            try { loader.RunScript(path); }
            catch (Exception ex) { pcs.Append(" EX:").Append(ex.Message.Replace('\n', ' ').Replace('\r', ' ')); }
            File.WriteAllText(@"E:\Projects\besm6.net\tests-run\namedub_main_states.txt", states.ToString());
            Assert.Inconclusive($"trace saved: {count} steps");
        }

        private static string FindFile(string relativePath, string fileName)
        {
            string dir = Directory.GetCurrentDirectory();
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