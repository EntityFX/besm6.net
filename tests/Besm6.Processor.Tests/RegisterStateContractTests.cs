using Besm6.Architecture.Machine;
using Besm6.Processor.Cpu;
using Besm6.Processor.Memory;

namespace Besm6.Tests;
using Processor = global::Besm6.Processor.Cpu.Processor;

[TestClass]
public sealed class RegisterStateContractTests
{
    [TestMethod]
    public void ExecutionRoleDoesNotRequireEditingArithmeticOrDiagnostics()
    {
        Assert.IsFalse(typeof(IRegisters).IsAssignableFrom(typeof(IProcessor)));
        Assert.IsFalse(typeof(IAlu).IsAssignableFrom(typeof(IProcessor)));
        Assert.IsFalse(typeof(Besm6.Architecture.Debug.IProcessorDebug).IsAssignableFrom(typeof(IProcessor)));
        Assert.IsNull(typeof(Processor).GetProperty("ExecutionProhibited"));
        Assert.IsNull(typeof(Processor).GetProperty("PreparedExecutionOnly"));
    }

    [TestMethod]
    [DataRow(0u)]
    [DataRow(63u)]
    [DataRow(64u)]
    [DataRow(uint.MaxValue)]
    public void RegisterEditorsPreserveTheSameSixBitMode(uint value)
    {
        var cpu = new Processor(new CoreMemory());
        IRegisters facade = cpu;
        IRegisters executionState = cpu.MutableState;
        facade.R = value;
        Assert.AreEqual(value & 63u, executionState.R);
        executionState.R = value;
        Assert.AreEqual(value & 63u, facade.R);
        cpu.SetR(value);
        Assert.AreEqual(value & 63u, cpu.State.R);
    }

    [TestMethod]
    public void LiveObservationCannotBeUsedAsARegisterEditor()
    {
        var cpu = new Processor(new CoreMemory());
        var view = cpu.State;
        Assert.IsFalse(view is IRegisters);
        Assert.IsTrue(typeof(IProcessorState).GetProperties().All(property => !property.CanWrite));
        cpu.K = 19;
        cpu.SetM(1, 123);
        Assert.AreEqual(19u, view.K);
        Assert.AreEqual(123u, view.M[1]);
        cpu.Reset();
        Assert.AreSame(view, cpu.State);
        Assert.AreEqual(1u, view.K);
        Assert.AreEqual(0u, view.M[1]);
    }

    [TestMethod]
    public void SnapshotRemainsImmutableAfterRegisterEditsAndReset()
    {
        var cpu = new Processor(new CoreMemory());
        cpu.K = 19;
        cpu.SetM(1, 123);
        cpu.R = 63;
        var snapshot = new ProcessorSnapshot(cpu.State);
        Assert.IsFalse((object)snapshot is IRegisters);
        Assert.IsTrue(typeof(ProcessorSnapshot).GetProperties().All(property => !property.CanWrite));
        var copy = snapshot.M.ToArray();
        copy[1] = 999;
        cpu.SetM(1, 456);
        cpu.Reset();
        Assert.AreEqual(19u, snapshot.K);
        Assert.AreEqual(63u, snapshot.R);
        Assert.AreEqual(123u, snapshot.M[1]);
        Assert.AreEqual(0u, cpu.M[1]);
    }
}
