using System.Text;
using Besm6.Android.Services;
using Besm6.Runtime.Storage;

namespace Besm6.Android.Tests;

[TestClass]
public sealed class RunSettingsTests
{
    [TestMethod]
    public void DefaultIsValid()
    {
        Assert.IsTrue(RunSettings.Default.TryValidate(out string? error), error);
    }

    [TestMethod]
    public void RejectsNegativeLimits()
    {
        var settings = RunSettings.Default with { InstructionLimit = -1 };
        Assert.IsFalse(settings.TryValidate(out _));
        Assert.IsFalse((settings with { InstructionLimit = 1, WallClockLimitMs = -1 }).TryValidate(out _));
    }

    [TestMethod]
    public void ZeroLimitsAreAllowed()
    {
        var settings = RunSettings.Default with { InstructionLimit = 0, WallClockLimitMs = 0 };
        Assert.IsTrue(settings.TryValidate(out _));
    }

    [TestMethod]
    public void DebugModeRejectsHardwareSupervisor()
    {
        var hardware = RunSettings.Default with
        {
            DebugMode = true,
            Model = MachineModel.SupervisorHardware,
        };
        Assert.IsFalse(hardware.TryValidate(out _));

        var dubna = hardware with { Model = MachineModel.Dubna };
        Assert.IsTrue(dubna.TryValidate(out _));
    }
}

[TestClass]
public sealed class RegisterFormatterTests
{
    [TestMethod]
    public void ShowsM0ThroughM15AndUnavailableBuffers()
    {
        uint[] indexes = Enumerable.Range(0, 16).Select(value => (uint)value).ToArray();
        var snapshot = new RegisterSnapshot("Dubna", "Functional", "Dubna",
            512, false, 0, 0, 0, 0, false, indexes, 42,
            [], []);

        string text = RegisterFormatter.Format(snapshot);

        StringAssert.Contains(text, "M0   00000");
        StringAssert.Contains(text, "M15  00017");
        StringAssert.Contains(text, "БРЧ");
        StringAssert.Contains(text, "недоступны");
        StringAssert.Contains(text, "БРС");
        StringAssert.Contains(text, "БРЗ");
    }

    [TestMethod]
    public void ShowsControlBitsAndBufferSlots()
    {
        var buffers = new[]
        {
            new BufferRegisterView("БРЧ", 0, 0x123, null, true, false,
                PendingWrite: null, Raw50: 1UL << 48),
            new BufferRegisterView("БРС", 1, 0x456, 512, true, null),
            new BufferRegisterView("БРЗ", 2, 0x789, null, null, true,
                PendingWrite: true),
        };
        var snapshot = new RegisterSnapshot("Buffered", "Functional", "Dubna",
            512, true, 0, 0, 0, 0, false, new uint[16], 7, buffers, []);

        string text = RegisterFormatter.Format(snapshot);

        StringAssert.Contains(text, "БРЧ[0]");
        StringAssert.Contains(text, "КР49/50=01");
        StringAssert.Contains(text, "БРС[1]");
        StringAssert.Contains(text, "БРЗ[2]");
        StringAssert.Contains(text, "ожидает записи");
    }
}

[TestClass]
public sealed class PlotterParserTests
{
    [TestMethod]
    public void WatanabeDrawsSegmentsWithPenColor()
    {
        var segments = PlotterParser.Parse("J2\nD0,0\nD10,10\nD20,20\n", "watanabe");

        Assert.AreEqual(1, segments.Count);
        Assert.AreEqual(1, segments[0].Color);
        Assert.AreEqual(0f, segments[0].X0);
        Assert.AreEqual(20f, segments[0].X1);
    }

    [TestMethod]
    public void EmptyPlotterDataHasNoSegments()
    {
        Assert.AreEqual(0, PlotterParser.Parse(string.Empty, "watanabe").Count);
        Assert.AreEqual(0, PlotterParser.Parse(null, "calcomp").Count);
    }
}

[TestClass]
public sealed class MachineRunServiceTests
{
    [TestMethod]
    public async Task RunsFortranHelloWorldOnLocalMachine()
    {
        string work = Path.Combine(Path.GetTempPath(), "besm6-android-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string job = Path.Combine(work, "job.dub");
            await File.WriteAllTextAsync(job, """
                *name test
                *fortran
                        program hello
                        print 1000
                        stop
                 1000   format('Hello, World!')
                        end
                *execute
                *end file
                """, new UTF8Encoding(false));

            using var service = new MachineRunService(TapeImage.DefaultTapesDir(), work);
            var output = new StringBuilder();
            var completion = new TaskCompletionSource<RunOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.OutputAppended += (_, text) =>
            {
                lock (output) output.Append(text);
            };
            service.Completed += outcome => completion.TrySetResult(outcome);

            await service.Start(job, RunSettings.Default);
            RunOutcome result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(180));

            Assert.IsTrue(result.Success, result.Error);
            lock (output) StringAssert.Contains(output.ToString().ToUpperInvariant(), "HELLO, WORLD!");
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }

    [TestMethod]
    public async Task DebugModeStartsPausedAndSupportsStepAndResume()
    {
        string work = Path.Combine(Path.GetTempPath(), "besm6-android-debug-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string job = Path.Combine(work, "job.dub");
            await File.WriteAllTextAsync(job, """
                *name test
                *fortran
                        program hello
                        print 1000
                        stop
                 1000   format('Hello, World!')
                        end
                *execute
                *end file
                """, new UTF8Encoding(false));

            using var service = new MachineRunService(TapeImage.DefaultTapesDir(), work);
            var paused = new SemaphoreSlim(0);
            var completion = new TaskCompletionSource<RunOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            service.PhaseChanged += phase =>
            {
                if (phase == RunPhase.Paused) paused.Release();
            };
            service.Completed += outcome => completion.TrySetResult(outcome);

            var settings = RunSettings.Default with { DebugMode = true, InstructionLimit = 20_000_000 };
            Task run = service.Start(job, settings);

            Assert.IsTrue(await paused.WaitAsync(TimeSpan.FromSeconds(60)), "debug run did not pause");
            service.Step();
            Assert.IsTrue(await paused.WaitAsync(TimeSpan.FromSeconds(60)), "step did not pause again");
            service.Resume();

            RunOutcome result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(180));
            await run;
            Assert.IsTrue(result.Success, result.Error);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }
}
