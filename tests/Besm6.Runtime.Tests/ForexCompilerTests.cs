using System;
using System.IO;
using System.Text;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Encoding;
using Besm6.Runtime.Loading;
using Besm6.Runtime.Machine;
using Besm6.Runtime.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests;

[TestClass]
public sealed class ForexCompilerTests
{
    [TestMethod]
    [DataRow(false, "*FOREX")]
    [DataRow(true, "*FORTRAN")]
    public void ScriptCard_ChangesOnlyWhenCompatibilityIsEnabled(bool legacy, string expected)
    {
        var machine = new MachineCore();
        var tapes = new TapeMountService(null);
        var loader = new JobProgramLoader(machine, tapes, null) { ForexAsFortran = legacy };
        loader.WriteScriptToDrum(JobParser.Parse(new[] { "*forex" }), new[] { "*FoReX", "*forex:option", "*forextra", "C *forex" });
        var drum = tapes.GetDrumByUnit(1)!;
        int offset = 0;
        string ReadCard()
        {
            var bytes = new System.Collections.Generic.List<byte>();
            bool end = false;
            do
            {
                long word = drum.ReadWord(offset++);
                for (int shift = 40; shift >= 0; shift -= 8)
                {
                    byte value = (byte)(word >> shift);
                    bytes.Add(value);
                    end |= value == 10;
                }
            } while (!end);
            return CosyCodec.DecodeCosy(bytes.ToArray())!.TrimEnd();
        }
        Assert.AreEqual(expected, ReadCard());
        Assert.AreEqual(expected + ":OPTION", ReadCard());
        Assert.AreEqual("*FOREXTRA", ReadCard());
        Assert.AreEqual("C *FOREX", ReadCard());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Configuration_SelectsActualCompilerAndHelloExecutes(bool legacy)
    {
        string path = Path.Combine(Path.GetTempPath(), "forex-config-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"forexAsFortran\":" + (legacy ? "true" : "false") + ",\"useWallClock\":false}");
            var config = Config.Load(path);
            config.Tapes = TapeImage.DefaultTapesDir();
            var loader = MachineFactory.CreateLoader(config);
            Assert.AreEqual(legacy, loader.ForexAsFortran);
            var output = new StringBuilder();
            loader.Output = value => output.Append(value);
            string[] cards = { "*name test", "*forex", "      PROGRAM HELLO", "      PRINT 1000", "      STOP", " 1000 FORMAT ('HELLO, WORLD!')", "      END", "*execute", "*end file" };
            loader.RunJob(JobParser.Parse(cards), cards);
            Assert.IsTrue(loader.HaltedByStop);
            string text = output.ToString();
            // Require guest output after the last separator, not a quoted
            // string echoed by the compiler before a translation failure.
            StringAssert.Contains(text[(text.LastIndexOf('≠') + 1)..], "HELLO, WORLD!");
            if (legacy)
            {
                StringAssert.Contains(text, "*FORTRAN");
                Assert.IsFalse(text.Contains("F O R E X"));
            }
            else
            {
                StringAssert.Contains(text, "*FOREX");
                StringAssert.Contains(text, "F O R E X");
                StringAssert.Contains(text, "4.13");
            }
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void DefaultConfiguration_LeavesForexNative()
    {
        Assert.IsFalse(new Config().ForexAsFortran);
        Assert.IsFalse(new DubnaLoader(new MachineCore()).ForexAsFortran);
    }

    [TestMethod]
    public void NativeForex_CompilesAssignmentAndExecutesArithmetic()
    {
        var loader = MachineFactory.CreateLoader(new Config { UseWallClock = false });
        var output = new StringBuilder();
        loader.Output = value => output.Append(value);
        // X=1.0 previously failed during translation at octal 40134:
        // three MONSYS words replaced by ASCII spaces decoded as illegal MOD.
        string[] cards = {
            "*name forex arithmetic", "*forex", "      PROGRAM HELLO",
            "      REAL X", "      X=1.0", "      X=X+2.0",
            "      PRINT 100,X", "  100 FORMAT ('FOREX RESULT=',F12.6)",
            "      STOP", "      END", "*execute", "*end file"
        };
        var result = loader.RunJob(JobParser.Parse(cards), cards);
        Assert.IsTrue(result.Success, result.ToString());
        Assert.IsTrue(loader.HaltedByStop);
        string text = output.ToString();
        StringAssert.Contains(text, "F O R E X");
        string program = text[(text.LastIndexOf('≠') + 1)..];
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(program,
            @"FOREX RESULT=\s*3\.000000"), program);
    }
}
