using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;
using Besm6.Processor.Cpu;
using Besm6.Processor.Memory;
using Besm6.Runtime.Devices;
using Besm6.Runtime.Extracodes;

namespace Besm6.Tests;
using Processor = global::Besm6.Processor.Cpu.Processor;

[TestClass]
public sealed class HostedServiceContractTests
{
    [TestMethod]
    public void DisabledTraceDoesNotOpenAHostWriter()
    {
        int opened = 0;
        using var trace = new HostedExtracodeTrace(false, () => { opened++; return new StringWriter(); });
        var cpu = new Processor(new CoreMemory());
        trace.Observe(new(Extracode.E50, 0, 0, 0, false), cpu);
        Assert.AreEqual(0, opened);
    }

    [TestMethod]
    public void TracePreservesCallContextAndOwnsOneLazyWriter()
    {
        int opened = 0;
        var writer = new OwnedWriter();
        var trace = new HostedExtracodeTrace(true, () => { opened++; return writer; });
        var cpu = new Processor(new CoreMemory()) { K = 16 };
        cpu.SetM(1, 7);
        var call = new ExtracodeCall(Extracode.E50, 0, 1, 0x7FC0, true);
        Assert.AreEqual(0, opened);
        trace.Observe(call, cpu);
        trace.Observe(call, cpu);
        Assert.AreEqual(1, opened);
        string expected = "00020 R: 01 050 77700 *50 -100(1) = 7" + Environment.NewLine;
        Assert.AreEqual(expected + expected, writer.ToString());
        trace.Dispose();
        Assert.IsTrue(writer.Disposed);
    }

    [TestMethod]
    public void TerminalInputFinishesPendingPrintBeforePromptAndRead()
    {
        var memory = new CoreMemory();
        var cpu = new Processor(memory);
        cpu.SetM(14, 8);
        memory.Write(8, new((6UL << 39) | (100UL << 24) | 100UL));
        var calls = new List<string>();
        var terminal = new HostedTerminalService(cpu, memory, new Plotter(), new Puncher(memory),
            output => calls.Add(output), prompt => { calls.Add("read"); return "Z"; },
            () => calls.Add("finish-print"));
        terminal.E71();
        CollectionAssert.AreEqual(new[] { "finish-print", "-\r", "read" }, calls);
        Assert.AreEqual(0x5A0000000000UL, memory.Read(100).Value);
    }

    [TestMethod]
    public void HangCounterIsPerServiceAndGuestOutputResetsOnlyItsOwnCounter()
    {
        var first = new HostedHangDetector { Enabled = true };
        var second = new HostedHangDetector { Enabled = true };
        for (int i = 0; i < 500; i++)
        {
            first.Observe(Extracode.E73, 1);
            second.Observe(Extracode.E73, 1);
        }
        Assert.ThrowsExactly<ProcessorException>(() => first.Observe(Extracode.E73, 1));
        first.Observe(Extracode.E64, 1);
        for (int i = 0; i < 500; i++) first.Observe(Extracode.E73, 1);
        Assert.ThrowsExactly<ProcessorException>(() => second.Observe(Extracode.E73, 1));
    }

    private sealed class OwnedWriter : StringWriter
    {
        internal bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
