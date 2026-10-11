using System.Text.Json;
using Besm6;

namespace Besm6.Tests;

[TestClass]
public sealed class GuestClockTests
{
    private static readonly Word48[] Program =
    [
        new(Besm6.Asm.Assembler.Asm("*50 70210, atx 102")),
        new(Besm6.Asm.Assembler.Asm("vtm 77760(2), ntr 6")),
        new(Besm6.Asm.Assembler.Asm("vtm 60000(1), utc")),
        new(Besm6.Asm.Assembler.Asm("ntr 6, vlm 4(1)")),
        new(Besm6.Asm.Assembler.Asm("vlm 3(2), *50 70077")),
        new(Besm6.Asm.Assembler.Asm("atx 100, *50 70210")),
        new(Besm6.Asm.Assembler.Asm("atx 101, stop")),
    ];

    [TestMethod]
    [DataRow(ExecutionSpeed.Max, false)]
    [DataRow(ExecutionSpeed.Max, true)]
    [DataRow(ExecutionSpeed.Original, false)]
    [DataRow(ExecutionSpeed.Original, true)]
    public void GuestClockUsesQuantizedModelTimeIndependentOfHostSpeedAndDate(ExecutionSpeed speed, bool wallDate)
    {
        var machine = new MachineCore();
        var loader = new DubnaLoader(machine)
        {
            Speed = speed, GuestClockEnabled = true, UseWallClock = wallDate,
            InstructionLimit = 1_000_000,
        };
        for (int run = 0; run < 2; run++)
        {
            machine.LoadProgram(Program, 1);
            Assert.IsTrue(loader.RunLoaded().Success);
            Assert.IsTrue(loader.HaltedByStop);
            Assert.AreEqual(Word48.Zero, machine.Memory.Read(66)); // 0102: run begins at zero.
            // 17 outer passes * 8193 inner passes * (NTR1 + VLM2), plus setup.
            Assert.AreEqual(Word48.FromDouble(.04), machine.Memory.Read(64)); // 0100: commercial time.
            Assert.AreEqual(Word48.FromDouble(.04), machine.Memory.Read(65)); // 0101: CPU time.
            Assert.IsTrue(loader.Statistics!.ModelSeconds is > .04 and < .06);
        }
    }

    [TestMethod]
    public void DefaultClockPreservesLegacyZeroResponsesWithoutEnablingCycleStatistics()
    {
        var machine = new MachineCore();
        var loader = new DubnaLoader(machine) { InstructionLimit = 1_000_000 };
        Assert.IsFalse(loader.GuestClockEnabled);
        machine.LoadProgram(Program, 1);
        Assert.IsTrue(loader.RunLoaded().Success);
        Assert.AreEqual(Word48.Zero, machine.Memory.Read(64));
        Assert.AreEqual(Word48.Zero, machine.Memory.Read(65));
        Assert.IsNull(loader.Statistics);
    }

    [TestMethod]
    public void ConfigurationMakesGuestClockAnExplicitIndependentOption()
    {
        Assert.IsFalse(new Config().GuestClock);
        var config = JsonSerializer.Deserialize<Config>("{\"guestClock\":true,\"useWallClock\":false,\"speed\":\"max\"}")!;
        Assert.IsTrue(config.GuestClock);
        Assert.IsFalse(config.UseWallClock);
        Assert.AreEqual(ExecutionSpeed.Max, config.Speed);
    }
}
