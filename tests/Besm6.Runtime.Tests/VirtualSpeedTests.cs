using Besm6.Runtime.Loading;
using System.Text.Json;
using Besm6;
using Besm6.Architecture.Numerics;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Execution;

namespace Besm6.Tests;

[TestClass]
public sealed class VirtualSpeedTests
{
    private sealed class HostTime : IExecutionTimeSource
    {
        public double ElapsedSeconds { get; set; } = 100;
        public void Sleep(int milliseconds) => Assert.Fail("Virtual must not pace execution");
        public void Spin() => Assert.Fail("Virtual must not pace execution");
    }

    [TestMethod]
    [DataRow(ExecutionSpeed.Max)]
    [DataRow(ExecutionSpeed.Original)]
    [DataRow(ExecutionSpeed.Virtual)]
    public void SpeedJsonRoundTripsAndMachineConfigurationAcceptsIt(ExecutionSpeed speed)
    {
        Assert.AreEqual(speed, JsonSerializer.Deserialize<ExecutionSpeed>(JsonSerializer.Serialize(speed)));
        Assert.AreEqual(speed, new MachineJobConfiguration { Speed = speed.ToString().ToLowerInvariant() }.ParseSpeed());
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void VirtualClockUsesHostTimeAndResetsWithoutChangingDateOrEventTicks(bool statistics, bool wallDate)
    {
        var machine = new MachineCore();
        var handler = new ExtracodeHandler(machine, _ => null, _ => null, _ => null, _ => { })
        { UseWallClock = wallDate };
        var time = new HostTime();
        var loop = new ExecutionLoop(machine, handler, null, null, null)
        {
            Speed = ExecutionSpeed.Virtual, GuestClockEnabled = true,
            CollectStatistics = statistics, GuestHostTimeSource = time,
        };
        loop.InstallHook();
        for (int run = 0; run < 2; run++)
        {
            machine.LoadProgram(new[]
            {
                new Word48(Assembler.Legacy.Assembler.Asm("*50 70210, utc")),
                new Word48(Assembler.Legacy.Assembler.Asm("atx 102, ntr 6")),
                new Word48(Assembler.Legacy.Assembler.Asm("*50 70077, utc")),
                new Word48(Assembler.Legacy.Assembler.Asm("atx 100, *50 70210")),
                new Word48(Assembler.Legacy.Assembler.Asm("atx 101, stop")),
            }, 1);
            ulong tick = machine.Clock.Tick;
            machine.Scheduler.Schedule(1, () =>
            {
                time.ElapsedSeconds += .079;
                Assert.AreEqual(.06, handler.GuestTimeSeconds!(), .000001, "callback clock");
            });
            Assert.IsTrue(loop.Run().Stopped);
            // A left-half extracode returns at the next word, preserving existing semantics.
            Assert.AreEqual(tick + 8, machine.Clock.Tick);
            Assert.AreEqual(Word48.Zero, machine.Memory.Read(66));
            Assert.AreEqual(Word48.FromDouble(.06), machine.Memory.Read(64));
            Assert.AreEqual(Word48.FromDouble(.06), machine.Memory.Read(65));
            Assert.AreEqual(wallDate, handler.UseWallClock);
            Assert.AreEqual(0d, handler.GuestTimeSeconds!()); // run clock no longer active.
            if (statistics)
            {
                Assert.IsNotNull(loop.Statistics);
                Assert.AreEqual(8L, loop.Statistics.CompletedInstructions);
                Assert.IsTrue(loop.Statistics.ModelSeconds < .02);
            }
            else Assert.IsNull(loop.Statistics); // no table accounting just to read host time.
            Assert.IsNull(machine.Cpu.InstructionAccounting);
        }
    }

    [TestMethod]
    public void VirtualWithoutGuestClockPreservesZeroTimerAndNoAccounting()
    {
        var machine = new MachineCore();
        var loader = new DubnaLoader(machine) { Speed = ExecutionSpeed.Virtual };
        machine.LoadProgram(new[] { new Word48(Assembler.Legacy.Assembler.Asm("*50 70210, atx 100")),
            new Word48(Assembler.Legacy.Assembler.Asm("stop")) }, 1);
        Assert.IsTrue(loader.RunLoaded().Stopped);
        Assert.AreEqual(Word48.Zero, machine.Memory.Read(64));
        Assert.IsNull(loader.Statistics);
    }

    [TestMethod]
    public void CallbackFailureReleasesVirtualClockBeforeNextRun()
    {
        var machine = new MachineCore();
        var handler = new ExtracodeHandler(machine, _ => null, _ => null, _ => null, _ => { });
        var time = new HostTime();
        var loop = new ExecutionLoop(machine, handler, null, null, null)
        { Speed = ExecutionSpeed.Virtual, GuestClockEnabled = true, GuestHostTimeSource = time };
        loop.InstallHook();
        machine.LoadProgram(new[] { new Word48(Assembler.Legacy.Assembler.Asm("ntr 6, stop")) }, 1);
        machine.Scheduler.Schedule(1, () => { time.ElapsedSeconds += 1; throw new IOException("callback"); });
        Assert.ThrowsExactly<SchedulerCallbackException>(() => loop.Run());
        Assert.AreEqual(0d, handler.GuestTimeSeconds!());
        machine.LoadProgram(new[] { new Word48(Assembler.Legacy.Assembler.Asm("*50 70210, utc")),
            new Word48(Assembler.Legacy.Assembler.Asm("stop")) }, 1);
        Assert.IsTrue(loop.Run().Stopped);
        Assert.AreEqual(Word48.Zero, machine.Cpu.State.A);
    }
}
