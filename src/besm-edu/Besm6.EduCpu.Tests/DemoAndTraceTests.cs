namespace Besm6.EduCpu.Tests;

[TestClass]
public class DemoAndTraceTests
{
    private static (List<Trace> Traces, Memory Mem, int Steps) RunDemoWithTraces()
    {
        Memory mem = new();
        (ushort entry, string _) = DemoProgram.Load(mem);
        Cpu cpu = new(mem, entry);
        List<Trace> traces = new();
        while (!cpu.Stopped)
        {
            traces.Add(cpu.Step());
        }

        return (traces, mem, cpu.Steps);
    }

    [TestMethod]
    public void Demo_RunsToStop_WithExpectedResult()
    {
        var (traces, mem, steps) = RunDemoWithTraces();
        Assert.IsTrue(steps <= 100);
        Assert.AreEqual(12UL, mem.Read(72).Raw); // 5 + 7 = 12 (десятично) = 14 восьмерично
        Assert.AreEqual("СТОП: процессор остановлен", traces[^1].Effect);
    }

    [TestMethod]
    public void Demo_TraceIsDeterministic()
    {
        var (t1, _, _) = RunDemoWithTraces();
        var (t2, _, _) = RunDemoWithTraces();
        Assert.AreEqual(t1.Count, t2.Count);
        for (int i = 0; i < t1.Count; ++i)
        {
            Assert.AreEqual(TraceFormatter.Format(t1[i]), TraceFormatter.Format(t2[i]));
        }
    }

    [TestMethod]
    public void Demo_UsesBothInstructionFormats()
    {
        var (traces, _, _) = RunDemoWithTraces();
        bool hasShort = traces.Any(t => t.Raw24 < (1u << 19));
        bool hasLong = traces.Any(t => (t.Raw24 & (1u << 19)) != 0);
        Assert.IsTrue(hasShort);
        Assert.IsTrue(hasLong);
    }
}
