using System.Collections.Concurrent;
using System.Text.Json;
using Besm6.AspNetCore.Web.Hosting;
using Besm6.AspNetCore.Web.Protocol;

namespace Besm6.AspNetCore.Web.Tests;

[TestClass]
public sealed class WorkerTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ConcurrentQueue<WorkerEvent> _events = new();
        private readonly ConcurrentQueue<string> _history = new();
        private readonly SemaphoreSlim _arrived = new(0);
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "besm6-web-tests", Guid.NewGuid().ToString("N"));
        public WorkerProcessHost Host { get; }

        public Fixture()
        {
            Host = new WorkerProcessHost(new WorkerOptions(DirectoryPath, "besm6-test-" + Guid.NewGuid().ToString("N")), e =>
            {
                _events.Enqueue(e);
                _history.Enqueue(e.Type == "state" ? "state:" + Data(e).GetProperty("status").GetString() : e.Type);
                _arrived.Release();
                return Task.CompletedTask;
            });
        }

        public async Task StartAsync(string job)
        {
            System.IO.Directory.CreateDirectory(DirectoryPath);
            await File.WriteAllTextAsync(Path.Combine(DirectoryPath, "job.dub"), job);
            await Host.StartAsync();
            await UntilAsync("state", e => Data(e).GetProperty("status").GetString() == "Idle");
        }

        public Task SendAsync(string type, string mode = "debug", long limit = 1000, bool guestClock = false) => Host.SendAsync(new WorkerCommand(type,
            Path.Combine(DirectoryPath, "job.dub"), mode, "max", limit, GuestClock: guestClock));

        public async Task<WorkerEvent> UntilAsync(string type, Func<WorkerEvent, bool>? match = null, bool allowError = false)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                try { await _arrived.WaitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    Assert.Fail("Timed out waiting for " + type + "; events: " + string.Join(", ", _history.TakeLast(20)));
                    throw;
                }
                Assert.IsTrue(_events.TryDequeue(out var value));
                if (value!.Type == "error" && !allowError) Assert.Fail(Data(value).GetProperty("message").GetString() ?? "Worker error");
                if (value.Type == type && (match is null || match(value))) return value;
            }
        }

        public static JsonElement Data(WorkerEvent e) => (JsonElement)e.Data!;

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            for (int attempt = 0; attempt < 10 && System.IO.Directory.Exists(DirectoryPath); attempt++)
            {
                try { System.IO.Directory.Delete(DirectoryPath, recursive: true); }
                catch (IOException) when (attempt < 9) { await Task.Delay(100); }
            }
        }
    }

    private const string TwoInstructions = "*assem\natx 1234\nstop\n*end file\n";

    private static ulong Asm(string text) => new Besm6.Assembler.Assembly.MadlenAssembler()
        .AssembleWord(text.Contains(',') ? text : text + ", atx");
    private static string Raw(IEnumerable<ulong> words, int address = 512) => "*trans-main:" + Convert.ToString(address, 8) + "\n"
        + string.Join('\n', words.Select(w => "`" + Convert.ToString((long)w, 8).PadLeft(16, '0'))) + "\n*end file\n";
    private static IEnumerable<ulong> Bytes(string text)
    {
        byte[] bytes = System.Text.Encoding.Latin1.GetBytes(text + '\0');
        for (int i = 0; i < bytes.Length; i += 6)
        {
            ulong word = 0;
            for (int j = 0; j < 6; j++) word = (word << 8) | (i + j < bytes.Length ? bytes[i + j] : 0UL);
            yield return word;
        }
    }

    [TestMethod]
    public async Task DebugStepAndFullRunComplete()
    {
        await using var f = new Fixture();
        await f.StartAsync(TwoInstructions);
        await f.SendAsync("start");
        await f.UntilAsync("state", e => Fixture.Data(e).GetProperty("status").GetString() == "Paused");
        await f.SendAsync("step");
        var registers = await f.UntilAsync("registers");
        Assert.AreEqual(1L, Fixture.Data(registers).GetProperty("instructions").GetInt64());
        await f.SendAsync("resume");
        var done = await f.UntilAsync("completed");
        Assert.IsTrue(Fixture.Data(done).GetProperty("success").GetBoolean());
        await f.SendAsync("start", "full");
        done = await f.UntilAsync("completed");
        Assert.IsTrue(Fixture.Data(done).GetProperty("stopped").GetBoolean());
    }

    [TestMethod]
    public async Task InvalidJobFailsAndWorkerAcceptsNextStart()
    {
        await using var f = new Fixture();
        await f.StartAsync("`9999\n");
        await f.SendAsync("start");
        await f.UntilAsync("state", e => Fixture.Data(e).GetProperty("status").GetString() == "Failed", allowError: true);
        await File.WriteAllTextAsync(Path.Combine(f.DirectoryPath, "job.dub"), TwoInstructions);
        await f.SendAsync("start", "full");
        var done = await f.UntilAsync("completed");
        Assert.IsTrue(Fixture.Data(done).GetProperty("success").GetBoolean());
    }

    [TestMethod]
    public async Task TwoWorkersHaveIndependentState()
    {
        await using var first = new Fixture();
        await using var second = new Fixture();
        await Task.WhenAll(first.StartAsync(TwoInstructions), second.StartAsync("*assem\nstop\n*end file\n"));
        await Task.WhenAll(first.SendAsync("start"), second.SendAsync("start"));
        await Task.WhenAll(first.UntilAsync("state", e => Fixture.Data(e).GetProperty("status").GetString() == "Paused"),
            second.UntilAsync("state", e => Fixture.Data(e).GetProperty("status").GetString() == "Paused"));
        await first.SendAsync("step");
        var firstRegs = await first.UntilAsync("registers");
        await second.Host.SendAsync(new WorkerCommand("snapshot"));
        var secondRegs = await second.UntilAsync("registers");
        Assert.AreEqual(1L, Fixture.Data(firstRegs).GetProperty("instructions").GetInt64());
        Assert.AreEqual(0L, Fixture.Data(secondRegs).GetProperty("instructions").GetInt64());
    }

    [TestMethod]
    public async Task OutputArrivesBeforeCompletion()
    {
        const string job = "*trans-main:1000\n`0064100200000000\n`0330000000000000\n`0000100400000000\n`0000000040000000\n`1324117200000000\n*execute\n*end file\n";
        await using var f = new Fixture();
        await f.StartAsync(job);
        await f.SendAsync("start", "full", 20_000_000);
        var output = await f.UntilAsync("output");
        StringAssert.Contains(Fixture.Data(output).GetProperty("text").GetString() ?? "", "HI");
        var done = await f.UntilAsync("completed");
        Assert.IsTrue(Fixture.Data(done).GetProperty("success").GetBoolean());
    }

    [TestMethod]
    [DataRow("full")]
    [DataRow("debug")]
    public async Task InstructionLimitReportsReason(string mode)
    {
        await using var f = new Fixture();
        await f.StartAsync("*assem\nuj 1000\n*end file\n");
        await f.SendAsync("start", mode, limit: 10);
        if (mode == "debug")
        {
            await f.UntilAsync("state", e => Fixture.Data(e).GetProperty("status").GetString() == "Paused");
            await f.SendAsync("resume");
        }
        var done = Fixture.Data(await f.UntilAsync("completed"));
        Assert.IsTrue(done.GetProperty("limitExceeded").GetBoolean());
        Assert.AreEqual(10L, done.GetProperty("instructions").GetInt64());
        Assert.IsFalse(string.IsNullOrWhiteSpace(done.GetProperty("errorMessage").GetString()));
    }

    [TestMethod]
    public async Task DebugPauseResumeAndStop()
    {
        await using var f = new Fixture();
        await f.StartAsync("*assem\nuj 1000\n*end file\n");
        await f.SendAsync("start", limit: 1_000_000);
        await f.UntilAsync("state", e => Fixture.Data(e).GetProperty("status").GetString() == "Paused");
        await f.SendAsync("resume");
        await f.UntilAsync("state", e => Fixture.Data(e).GetProperty("status").GetString() == "Running");
        await f.SendAsync("pause");
        await f.UntilAsync("state", e => Fixture.Data(e).GetProperty("status").GetString() == "Paused");
        await f.SendAsync("step");
        await f.UntilAsync("registers");
        await f.SendAsync("stop");
        await f.UntilAsync("state", e => Fixture.Data(e).GetProperty("status").GetString() == "Halted");
    }

    [TestMethod]
    [DataRow("full", false)]
    [DataRow("full", true)]
    [DataRow("debug", false)]
    [DataRow("debug", true)]
    public async Task GuestClockAccumulatesAcrossExecutionAndDebugSteps(string mode, bool enabled)
    {
        string[] program = ["*50 70210, atx 102", "vtm 77760(2), ntr 6", "vtm 60000(1), utc",
            "ntr 6, vlm 4(1)", "vlm 3(2), *50 70077", "atx 100, *50 70210", "atx 101, stop"];
        await using var f = new Fixture();
        await f.StartAsync(Raw(program.Select(Asm), 1));
        await f.SendAsync("start", mode, 1_000_000, guestClock: enabled);
        if (mode == "debug")
        {
            await f.UntilAsync("state", e => Fixture.Data(e).GetProperty("status").GetString() == "Paused");
            await f.SendAsync("step");
            await f.UntilAsync("state", e => Fixture.Data(e).GetProperty("status").GetString() == "Paused");
            await f.SendAsync("resume");
        }
        await f.UntilAsync("completed");
        await f.Host.SendAsync(new WorkerCommand("snapshot", MemoryAddress: 64, MemoryCount: 3));
        var words = Fixture.Data(await f.UntilAsync("memory")).GetProperty("words");
        string expected = enabled ? Besm6.Architecture.Numerics.Word48.FromDouble(.04).Value.ToString("X12") : "000000000000";
        Assert.AreEqual(expected, words[0].GetProperty("hex").GetString());
        Assert.AreEqual(expected, words[1].GetProperty("hex").GetString());
    }

    [TestMethod]
    [DataRow("watanabe")]
    [DataRow("tektronix")]
    [DataRow("calcomp")]
    public async Task PlotterDeviceOutputReachesHost(string type)
    {
        string data = type == "watanabe" ? "M0,0\nD100,100\n" : "XY";
        string job = type == "calcomp"
            ? Raw([Asm("xta 1002, *57 2"), Asm("stop"), 10UL])
            : Raw(new[] { Asm("xta 1002, *61 77777"), Asm("stop"), 515UL | (type == "tektronix" ? 0x300UL << 36 : 0UL) }.Concat(Bytes(data)));
        await using var f = new Fixture();
        await f.StartAsync(job);
        await f.SendAsync("start", "full");
        var output = Fixture.Data(await f.UntilAsync("output", e => Fixture.Data(e).GetProperty("channel").GetString() == type));
        Assert.AreEqual(type == "calcomp" ? "\n" : data, output.GetProperty("text").GetString());
        var done = Fixture.Data(await f.UntilAsync("completed"));
        Assert.IsTrue(done.GetProperty("success").GetBoolean());
    }

    [TestMethod]
    public async Task TerminalUsesItsOwnOutputChannel()
    {
        var job = Raw(new[] { Asm("*71 1002"), Asm("stop"), (4UL << 39) | (515UL << 24) }.Concat(Bytes("HI")));
        await using var f = new Fixture();
        await f.StartAsync(job);
        await f.SendAsync("start", "full");
        var output = Fixture.Data(await f.UntilAsync("output"));
        Assert.AreEqual("terminal", output.GetProperty("channel").GetString());
        Assert.AreEqual("HI\n", output.GetProperty("text").GetString());
        Assert.IsTrue(Fixture.Data(await f.UntilAsync("completed")).GetProperty("success").GetBoolean());
    }

    [TestMethod]
    public async Task PunchOutputIsFlushedAndCanBeProducedAgain()
    {
        var job = Raw(new[] { Asm("*71 1002"), Asm("stop"), (1UL << 39) | (515UL << 24) | 538UL }
            .Concat(Enumerable.Range(0, 24).Select(i => (ulong)i * 7)));
        await using var f = new Fixture();
        await f.StartAsync(job);
        string? previous = null;
        for (int run = 0; run < 2; run++)
        {
            await f.SendAsync("start", "full");
            var output = Fixture.Data(await f.UntilAsync("output", e => Fixture.Data(e).GetProperty("channel").GetString() == "punch"));
            string text = output.GetProperty("text").GetString() ?? "";
            Assert.IsTrue(text.Length > 0);
            if (previous is not null) Assert.AreEqual(previous, text);
            previous = text;
            Assert.IsTrue(Fixture.Data(await f.UntilAsync("completed")).GetProperty("success").GetBoolean());
        }
    }

    [TestMethod]
    [DataRow("full")]
    [DataRow("debug")]
    public async Task StatisticsAreOptionalAndCountTheSameInstructions(string mode)
    {
        await using var f = new Fixture();
        await f.StartAsync(TwoInstructions);
        await f.Host.SendAsync(new WorkerCommand("start", Path.Combine(f.DirectoryPath, "job.dub"), mode,
            Statistics: true));
        if (mode == "debug")
        {
            await f.UntilAsync("state", e => Fixture.Data(e).GetProperty("status").GetString() == "Paused");
            await f.SendAsync("resume");
        }
        var statistics = Fixture.Data(await f.UntilAsync("statistics"));
        var completed = Fixture.Data(await f.UntilAsync("completed"));
        Assert.AreEqual(completed.GetProperty("instructions").GetInt64(), statistics.GetProperty("completedInstructions").GetInt64());
        Assert.IsTrue(statistics.GetProperty("elapsedSeconds").GetDouble() > 0);
        Assert.IsTrue(statistics.GetProperty("modelCycles").GetInt64() > 0);
    }

    [TestMethod]
    [DataRow("full")]
    [DataRow("debug")]
    public async Task ResetClearsTheMachineAndPreservesTheUploadedJob(string mode)
    {
        await using var f = new Fixture();
        await f.StartAsync("*assem\nuj 1000\n*end file\n");
        await f.SendAsync("start", mode, limit: 1_000_000_000);
        await f.UntilAsync("state", e => Fixture.Data(e).GetProperty("status").GetString() == (mode == "full" ? "Running" : "Paused"));
        await f.SendAsync("reset");
        await f.UntilAsync("reset");
        var registers = Fixture.Data(await f.UntilAsync("registers"));
        Assert.AreEqual(0L, registers.GetProperty("instructions").GetInt64());
        Assert.AreEqual("000000000000", registers.GetProperty("a").GetString());
        var memory = Fixture.Data(await f.UntilAsync("memory"));
        Assert.IsTrue(memory.GetProperty("words").EnumerateArray().All(w => w.GetProperty("hex").GetString() == "000000000000"));
        await f.UntilAsync("state", e => Fixture.Data(e).GetProperty("status").GetString() == "Idle");
        Assert.IsTrue(File.Exists(Path.Combine(f.DirectoryPath, "job.dub")));
        await f.SendAsync("start", "debug");
        await f.UntilAsync("state", e => Fixture.Data(e).GetProperty("status").GetString() == "Paused");
    }
}
