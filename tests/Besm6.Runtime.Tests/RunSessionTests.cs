using Besm6.Runtime.Configuration;
using Besm6.Runtime.Machine;

namespace Besm6.Tests;

[TestClass]
public sealed class RunSessionTests
{
    [TestMethod]
    public void DefaultsComeFromConfigAndKeepZeroLimit()
    {
        var config = new Config { DefaultLimit = 0, Speed = ExecutionSpeed.Original, GuestClock = true };
        var settings = RunSessionSettings.FromConfig(config);
        Assert.AreEqual(0L, settings.InstructionLimit);
        Assert.AreEqual(ExecutionSpeed.Original, settings.Speed);
        Assert.IsTrue(settings.GuestClock);
    }

    [TestMethod]
    public void OneConstructionPathAppliesHostedSupervisorSettings()
    {
        var config = new Config();
        var session = MachineFactory.CreateRunSession(config, new RunSessionSettings(
            ExecutionSpeed.Max, 0, BufferedMemory: false, SupervisorHostedServices: true,
            CollectStatistics: true));
        Assert.IsTrue(session.Machine.UsesHostedSupervisorServices);
        Assert.AreEqual(0L, session.Loader.InstructionLimit);
        Assert.IsTrue(session.Loader.CollectStatistics);
        Assert.AreEqual(Besm6.Architecture.Machine.ProcessorProfile.Supervisor, session.Machine.Cpu.Profile);
    }

    [TestMethod]
    public void IncompatibleBackendSelectionIsRejectedByRuntime()
    {
        Assert.ThrowsExactly<ArgumentException>(() => MachineFactory.CreateRunSession(
            new Config(), new RunSessionSettings(
                ExecutionSpeed.Max, 100, BufferedMemory: true, SupervisorHostedServices: true)));
    }

    [TestMethod]
    public void NegativeLimitIsRejectedBeforeAssetsAreLoaded()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => MachineFactory.CreateRunSession(
            new Config(), new RunSessionSettings(ExecutionSpeed.Max, -1)));
    }
}
