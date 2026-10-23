using Besm6.Architecture.Numerics;
using Besm6.Assembler.Assembly;
using Besm6.Processor.Memory;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Machine;

namespace Besm6.Runtime.Tests;

[TestClass]
public sealed class HostedBufferedMemoryTests
{
    [TestMethod]
    public void FactoryHostedBuffersExecuteSelfModifiedCodeFromPendingBrzAndInvalidateWarmBrs()
    {
        var assembler = new MadlenAssembler();
        Word48 Code(string text) => new(assembler.AssembleWord(text));
        var machine = MachineFactory.CreateMachine(new Config { BufferedMemory = true });
        machine.LoadProgram(new[] { Code("xta 13, atx 12"), Code("uj 12, atx"),
            Code("vtm 1(2), stop"), Code("vtm 7(2), stop") }, 8);
        machine.BufferedMemory!.FetchInstruction(10, false); // old command already in BRS
        for(int i=0;i<4;i++) Assert.IsFalse(machine.Step());
        Assert.IsTrue(machine.Step());
        Assert.AreEqual(7u, machine.Cpu.GetM(2));
        Assert.AreEqual(Code("vtm 7(2), stop").Value, machine.Memory.Read(10).Value);
        Assert.AreEqual(1, machine.BufferedMemory.Buffers.PendingWriteCount);
    }

    [TestMethod]
    public void StrictBackendStillRejectsNumberAsCommandWhileHostedZeroInitializationIsExecutable()
    {
        var strict = new BufferedMemoryBackend();
        Assert.Throws<MemoryControlException>(() => strict.FetchInstruction(955, false));
        var hosted = new BufferedMemoryBackend(hostedCompatibility: true);
        Assert.AreEqual(0UL, hosted.FetchInstruction(955, false).Value);
        hosted.Write(955, new Word48(42));
        Assert.AreEqual(42UL, hosted.FetchInstruction(955, false).Value);
        Assert.AreEqual(42UL, hosted.Read(955).Value);
    }
}
