using System.Globalization;
using Besm6.Architecture.Isa;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Loading;
using Besm6.Runtime.Profiling;

namespace Besm6.Tests;

[TestClass]
public sealed class HardwareSupervisorDefaultsTests
{
    [TestMethod]
    public void SupervisorHardwareDefaultsConfigureEveryExtracodeDuration()
    {
        var defaults = HardwareSupervisorDefaults.Create(ExecutionSpeed.Max, 1_000);
        var durations = defaults.Timing!.ControlDurationsNs;
        ulong extracode = (ulong)Besm6Timing.ExtracodeCycles * Besm6Timing.NanosecondsPerCycle;

        for (int code = 0x28; code <= 0x3F; code++)
        {
            string key = code.ToString(CultureInfo.InvariantCulture);
            Assert.IsTrue(durations.TryGetValue(key, out ulong value), $"Missing UU duration for extracode {key}.");
            Assert.AreEqual(extracode, value, $"Wrong UU duration for extracode {key}.");
        }
        Assert.AreEqual(extracode, durations["128"]);
        Assert.AreEqual(extracode, durations["136"]);

        Assert.AreEqual((ulong)Besm6Timing.CyclesOf(Opcode.Stop) * Besm6Timing.NanosecondsPerCycle, durations["Stop"]);
        Assert.AreEqual((ulong)Besm6Timing.CyclesOf(Opcode.Xta) * Besm6Timing.NanosecondsPerCycle, durations["Xta"]);

        // The numeric keys must survive MachineTimingConfiguration parsing into the
        // supervisor execution configuration instead of being rejected as unknown opcodes.
        _ = defaults.Timing.Create();
    }
}
