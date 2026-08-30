# Reliable Execution Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make supported simulator jobs start deterministically from a normal checkout, route raw images correctly, and make CLI health checks report failures honestly.

**Architecture:** Resolve resource paths once at the configuration/factory boundary, validate required volumes at the loader boundary, and keep raw execution independent of MONSYS. Exercise CLI behavior through the existing public commands and the real program entry point, without adding a second parser or a parallel execution path.

**Tech Stack:** C# 12, .NET 8, MSTest 4, PowerShell verification commands, existing Python `unittest` trace comparator.

**Spec:** `docs/superpowers/specs/2026-08-31-reliable-execution-foundation-design.md`

## Global Constraints

- Execute this plan in an isolated `codex/` worktree created with `superpowers:using-git-worktrees`.
- Follow red-green TDD for every production behavior change and record the expected RED failure before implementation.
- Do not add or copy tape images; developer checkout discovery may use `ref/dubna/tapes`, while installed use remains explicitly configurable.
- Raw jobs mount only their explicit `*tape` cards and never require implicit MONSYS.
- Source/assembler OS jobs require MONSYS and must fail before the first instruction when a required image is absent.
- `check` certifies parsing and execution termination only; exact program output remains the responsibility of golden integration tests.
- Preserve current processor, canonical trace, E64 buffering, and tape lifecycle semantics.

---

### Task 1: Deterministic configuration-relative resource resolution

**Files:**
- Create: `src/besm6.net/tests/Besm6.Tests/ConfigTests.cs`
- Modify: `src/besm6.net/Cli/Config.cs:1-88`
- Verify only: `src/besm6.net/Cli/MachineFactory.cs:24-35`

**Interfaces:**
- Consumes: `TapeImage.DefaultTapesDir() : string`.
- Produces: `Config.Load(string? path = null)`, retaining the public signature; `Config.ResolvePath(string relative) : string`; private `Config.SourceDirectory : string?` populated only for a loaded file.

- [ ] **Step 1: Add tests for explicit missing configuration and config-relative paths**

```csharp
using System;
using System.IO;
using Besm6.Loader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ConfigTests
{
    [TestMethod]
    public void Load_ExplicitMissingPath_ThrowsFileNotFoundException()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        Assert.Throws<FileNotFoundException>(() => Config.Load(path));
    }

    [TestMethod]
    public void ResolvePath_RelativeResource_UsesConfigurationDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "besm6_cfg_" + Guid.NewGuid().ToString("N"));
        string images = Path.Combine(root, "images");
        Directory.CreateDirectory(images);
        string configPath = Path.Combine(root, "besm6.json");
        File.WriteAllText(configPath, "{\"tapes\":\"images\"}");
        try
        {
            Config config = Config.Load(configPath);
            Assert.AreEqual(Path.GetFullPath(images), config.ResolvePath(config.Tapes!));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ResolvePath_ConventionalTapes_UsesCheckoutDiscovery()
    {
        string root = Path.Combine(Path.GetTempPath(), "besm6_tapes_" + Guid.NewGuid().ToString("N"));
        string tapes = Path.Combine(root, "ref", "dubna", "tapes");
        Directory.CreateDirectory(tapes);
        string previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = root;
            Assert.AreEqual(Path.GetFullPath(tapes), new Config().ResolvePath("tapes"));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
            Directory.Delete(root, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run the new tests and verify RED**

Run:

```powershell
dotnet test src/besm6.net/besm6.net.sln -c Release --filter "FullyQualifiedName~ConfigTests"
```

Expected: the missing explicit file does not throw, the config-relative path resolves under the current directory, and conventional discovery does not participate in `Config.ResolvePath`.

- [ ] **Step 3: Record the loaded config directory and implement ordered resolution**

Add `using Besm6.Loader;` and `using System.Text.Json.Serialization;`, then implement:

```csharp
[JsonIgnore]
private string? SourceDirectory { get; set; }

public static Config Load(string? path = null)
{
    bool explicitPath = path != null;
    if (path == null)
    {
        path = Path.Combine(AppContext.BaseDirectory, "besm6.json");
        if (!File.Exists(path))
            path = "besm6.json";
    }

    if (!File.Exists(path))
    {
        if (explicitPath)
            throw new FileNotFoundException("Configuration file not found", path);
        return new Config();
    }

    string fullPath = Path.GetFullPath(path);
    string json = File.ReadAllText(fullPath);
    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    Config config = JsonSerializer.Deserialize<Config>(json, opts) ?? new Config();
    config.SourceDirectory = Path.GetDirectoryName(fullPath);
    return config;
}

public string ResolvePath(string relative)
{
    if (Path.IsPathRooted(relative) && (File.Exists(relative) || Directory.Exists(relative)))
        return Path.GetFullPath(relative);

    if (SourceDirectory != null)
    {
        string fromConfig = Path.Combine(SourceDirectory, relative);
        if (File.Exists(fromConfig) || Directory.Exists(fromConfig))
            return Path.GetFullPath(fromConfig);
    }

    if (File.Exists(relative) || Directory.Exists(relative))
        return Path.GetFullPath(relative);

    string fromApp = Path.Combine(AppContext.BaseDirectory, relative);
    if (File.Exists(fromApp) || Directory.Exists(fromApp))
        return Path.GetFullPath(fromApp);

    if (string.Equals(relative.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                      "tapes", StringComparison.OrdinalIgnoreCase))
        return Path.GetFullPath(TapeImage.DefaultTapesDir());

    return Path.GetFullPath(SourceDirectory == null
        ? relative
        : Path.Combine(SourceDirectory, relative));
}
```

Keep `MachineFactory.CreateLoader` calling `cfg.ResolvePath(cfg.Tapes)`; the conventional configured value now resolves to the discovered checkout directory, while custom missing paths remain explicit for loader diagnostics.

- [ ] **Step 4: Run ConfigTests and the existing tape tests**

Run:

```powershell
dotnet test src/besm6.net/besm6.net.sln -c Release --filter "FullyQualifiedName~ConfigTests|FullyQualifiedName~TapeIdTests"
```

Expected: PASS.

- [ ] **Step 5: Commit the configuration boundary**

```powershell
git add src/besm6.net/Cli/Config.cs src/besm6.net/tests/Besm6.Tests/ConfigTests.cs
git commit -m "Resolve simulator resources deterministically"
```

---

### Task 2: Fail-fast tape mounting with raw/OS separation

**Files:**
- Modify: `src/besm6.net/Loader/DubnaLoader.cs:145-174,317-334,408-509,587-593`
- Modify: `src/besm6.net/tests/Besm6.Tests/LoaderTests.cs` in `DubnaLoaderTests`

**Interfaces:**
- Consumes: `MountTape(int unit, long tapeId, bool writePermit = false) : bool`.
- Produces: private `MountRequestedTapes(DubJob job) : void`; private `EnsureMonsysTape() : void`; public `MountScriptTapes(DubJob job) : void` remains and calls both helpers.

- [ ] **Step 1: Add RED tests for missing MONSYS and unknown explicit tape**

Append to `DubnaLoaderTests`:

```csharp
[TestMethod]
public void MountScriptTapes_MissingMonsys_ThrowsBeforeExecution()
{
    string empty = Path.Combine(Path.GetTempPath(), "besm6_empty_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(empty);
    try
    {
        var loader = new DubnaLoader(new MachineCore(), empty);
        ProcessorException ex = Assert.Throws<ProcessorException>(
            () => loader.MountScriptTapes(new DubJob()));
        StringAssert.Contains(ex.Message, "MONSYS");
    }
    finally
    {
        Directory.Delete(empty, recursive: true);
    }
}

[TestMethod]
public void MountScriptTapes_UnknownRequestedTape_Throws()
{
    string empty = Path.Combine(Path.GetTempPath(), "besm6_empty_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(empty);
    try
    {
        DubJob job = JobParser.Parse(new[] { "*tape:5/no-such-volume" });
        var loader = new DubnaLoader(new MachineCore(), empty);
        ProcessorException ex = Assert.Throws<ProcessorException>(() => loader.MountScriptTapes(job));
        StringAssert.Contains(ex.Message, "no-such-volume");
    }
    finally
    {
        Directory.Delete(empty, recursive: true);
    }
}
```

- [ ] **Step 2: Run the two tests and verify RED**

Run:

```powershell
dotnet test src/besm6.net/besm6.net.sln -c Release --filter "Name~MountScriptTapes_MissingMonsys|Name~MountScriptTapes_UnknownRequestedTape"
```

Expected: both tests fail because mount failure and unknown tape names are silently ignored.

- [ ] **Step 3: Split requested mounts from implicit MONSYS and validate both**

Replace the current body of `MountScriptTapes` with these helpers:

```csharp
private void MountRequestedTapes(DubJob job)
{
    foreach (TapeMount mount in job.TapeMounts)
    {
        long tapeId = TapeImage.TapeIdByName(mount.Name, mount.Channel);
        if (tapeId == 0)
            throw new ProcessorException($"Unknown tape '{mount.Name}' on channel {Convert.ToString(mount.Channel, 8)}");

        int unit = 24 + (mount.Channel & 0x1F);
        if (!MountTape(unit, tapeId))
            throw new ProcessorException(
                $"Cannot mount tape '{mount.Name}' (0x{tapeId:X12}) on unit {Convert.ToString(unit, 8)} from '{_tapesDir ?? TapeImage.DefaultTapesDir()}'");
    }
}

private void EnsureMonsysTape()
{
    if (_disksByUnit.TryGetValue(24, out TapeImage? mounted) &&
        mounted.VolumeId == TapeImage.TapeMonsys)
        return;

    if (!MountTape(24, TapeImage.TapeMonsys))
        throw new ProcessorException(
            $"Cannot mount MONSYS tape (0x{TapeImage.TapeMonsys:X12}) on unit 30 from '{_tapesDir ?? TapeImage.DefaultTapesDir()}'");
}

public void MountScriptTapes(DubJob job)
{
    MountRequestedTapes(job);
    EnsureMonsysTape();
}
```

Do not alter `MountTape` range, duplicate-id, or occupied-unit behavior.

- [ ] **Step 4: Run mount tests and existing tape lifecycle regressions**

Run:

```powershell
dotnet test src/besm6.net/besm6.net.sln -c Release --filter "Name~MountScriptTapes|Name~MountTape_|Name~ReleaseTapes_"
```

Expected: PASS.

- [ ] **Step 5: Commit fail-fast mounts**

```powershell
git add src/besm6.net/Loader/DubnaLoader.cs src/besm6.net/tests/Besm6.Tests/LoaderTests.cs
git commit -m "Fail fast when required tapes are unavailable"
```

---

### Task 3: Route raw images before MONSYS

**Files:**
- Modify: `src/besm6.net/Loader/DubnaLoader.cs:408-509,514-581`
- Modify: `src/besm6.net/tests/Besm6.Tests/LoaderTests.cs` in `DubnaLoaderTests`
- Modify: `src/besm6.net/tests/Besm6.Tests/IntegrationTests.cs:84-100`

**Interfaces:**
- Consumes: private `MountRequestedTapes(DubJob job)` from Task 2.
- Produces: `RunJob` and `LoadScript` raw branches that do not call `EnsureMonsysTape`; `RunRawWords` honors explicit tape cards.

- [ ] **Step 1: Add a unit regression for raw words plus `*execute`**

Append to `DubnaLoaderTests`:

```csharp
[TestMethod]
public void RunJob_RawWordsWithExecute_DoesNotBootMonsys()
{
    string empty = Path.Combine(Path.GetTempPath(), "besm6_empty_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(empty);
    try
    {
        const int baseAddr = 512;
        ulong stop24 = (1UL << 20) | (0xD8UL << 12);
        var job = new DubJob { TransMain = baseAddr, Execute = string.Empty };
        job.RawWords.Add((long)((stop24 << 24) & 0xFFFFFFFFFFFFUL));
        var loader = new DubnaLoader(new MachineCore(), empty) { InstructionLimit = 10 };

        LoadResult result = loader.RunJob(job, Array.Empty<string>());

        Assert.IsTrue(result.Success, result.ToString());
        Assert.IsTrue(result.Stopped);
        Assert.AreEqual(1L, result.Instructions);
    }
    finally
    {
        Directory.Delete(empty, recursive: true);
    }
}
```

- [ ] **Step 2: Enable the committed raw integration test with its actual expected output**

In `IntegrationTests.cs`, remove the obsolete `Ignore`, rename the method, and assert the example's documented `HI` payload:

```csharp
[TestMethod]
public void RawHelloDub_ProducesHi()
{
    string? path = FindFileInParentDirs("src/besm6.net/tests/raw", "hello.dub");
    Assert.IsNotNull(path, "File src/besm6.net/tests/raw/hello.dub not found");

    LoadResult result = _loader.RunScript(path);
    Assert.IsTrue(result.Success, result.ToString());
    StringAssert.Contains(_output.ToString(), "HI");
}
```

- [ ] **Step 3: Run both regressions and verify RED**

Run:

```powershell
dotnet test src/besm6.net/besm6.net.sln -c Release --filter "Name~RunJob_RawWordsWithExecute|Name~RawHelloDub_ProducesHi"
```

Expected: failure before raw execution because `job.Execute` selects the MONSYS path (or because implicit MONSYS is requested in the empty tape directory).

- [ ] **Step 4: Move mount selection into the chosen execution path**

Make these control-flow changes:

```csharp
public LoadResult RunJob(DubJob job, IEnumerable<string> rawLines)
{
    _machine.Reset();

    if (job.RawWords.Count > 0)
        return RunRawWords(job);

    if (job.Execute == null && job.AssemProgram.Count > 0)
        return RunAssem(job);

    WriteScriptToDrum(job, rawLines);
    return BootAndRun(job);
}
```

At the beginning of both local paths, mount only explicitly requested tapes:

```csharp
public LoadResult RunRawWords(DubJob job)
{
    MountRequestedTapes(job);
    // existing load, PC, hook, and RunBounded code follows
}

public LoadResult RunAssem(DubJob job)
{
    MountRequestedTapes(job);
    // existing assembler path follows
}
```

In `LoadScript`, remove the unconditional `MountScriptTapes(job)`. Call
`MountRequestedTapes(job)` in the raw and local assembler branches, and call
`MountScriptTapes(job)` only in the final MONSYS branch before `BootMsDubna()`.
Keep `BootAndRun(job)` as the single OS-run location that calls
`MountScriptTapes(job)`.

- [ ] **Step 5: Run raw and loader tests**

Run:

```powershell
dotnet test src/besm6.net/besm6.net.sln -c Release --filter "Name~RunRawWords|Name~RunJob_RawWordsWithExecute|Name~RawHelloDub_ProducesHi|Name~LoadScript"
```

Expected: PASS, with the raw hello test no longer skipped.

- [ ] **Step 6: Commit raw-path selection**

```powershell
git add src/besm6.net/Loader/DubnaLoader.cs src/besm6.net/tests/Besm6.Tests/LoaderTests.cs src/besm6.net/tests/Besm6.Tests/IntegrationTests.cs
git commit -m "Run raw program images without booting MONSYS"
```

---

### Task 4: Make `besm6 help` safe

**Files:**
- Create: `src/besm6.net/tests/Besm6.Tests/CliContractTests.cs`
- Modify: `src/besm6.net/Program.cs:11-24`

**Interfaces:**
- Consumes: existing `ICommand` implementations.
- Produces: the same `Program.Main(string[] args) : int` behavior, with a registry that never contains null.

- [ ] **Step 1: Add a real entry-point regression using reflection**

```csharp
using System;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CliContractTests
{
    [TestMethod]
    public void Help_PrintsAllCommandsAndReturnsZero()
    {
        Type program = typeof(Config).Assembly.GetType("Besm6.Program", throwOnError: true)!;
        MethodInfo main = program.GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic)!;
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            int exitCode = (int)main.Invoke(null, new object[] { new[] { "help" } })!;
            Assert.AreEqual(0, exitCode);
            StringAssert.Contains(stdout.ToString(), "run");
            StringAssert.Contains(stdout.ToString(), "help");
            Assert.AreEqual(string.Empty, stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
```

- [ ] **Step 2: Run the help regression and verify RED**

Run:

```powershell
dotnet test src/besm6.net/besm6.net.sln -c Release --filter "Name~Help_PrintsAllCommandsAndReturnsZero"
```

Expected: `TargetInvocationException` whose inner exception is the current `NullReferenceException` in `HelpCommand.Execute`.

- [ ] **Step 3: Build the command registry without a null sentinel**

Replace the registry construction in `Program.Main` with:

```csharp
var commandList = new List<ICommand>
{
    new RunCommand(),
    new AsmCommand(),
    new DisasmCommand(),
    new CheckCommand(),
    new TuiCommand(),
};
commandList.Add(new HelpCommand(commandList));
var commands = new Dictionary<string, ICommand>(StringComparer.OrdinalIgnoreCase);
foreach (ICommand command in commandList)
    commands.Add(command.Name, command);
```

- [ ] **Step 4: Run the CLI contract test**

Run:

```powershell
dotnet test src/besm6.net/besm6.net.sln -c Release --filter "FullyQualifiedName~CliContractTests"
```

Expected: PASS.

- [ ] **Step 5: Commit the help fix**

```powershell
git add src/besm6.net/Program.cs src/besm6.net/tests/Besm6.Tests/CliContractTests.cs
git commit -m "Build CLI command registry without null entries"
```

---

### Task 5: Make `besm6 check` report execution failures honestly

**Files:**
- Modify: `src/besm6.net/Cli/CheckCommand.cs:43-79`
- Modify: `src/besm6.net/tests/Besm6.Tests/CliContractTests.cs`

**Interfaces:**
- Consumes: `JobParser.ParseFile`, `DubnaLoader.RunJob`, `LoadResult`.
- Produces: `CheckCommand.Execute(string[] args) : int`, returning 1 when any file has a parse error, runtime failure, or instruction-limit termination.

- [ ] **Step 1: Add a check regression for instruction-limit exit status**

Append to `CliContractTests`:

```csharp
[TestMethod]
public void Check_InstructionLimit_ReturnsFailure()
{
    string root = Path.Combine(Path.GetTempPath(), "besm6_check_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        ulong jump = Besm6.Asm.Assembler.Asm("uj 1000");
        ulong word = (jump << 24) | jump;
        string octal = Convert.ToString((long)word, 8).PadLeft(16, '0');
        File.WriteAllLines(Path.Combine(root, "loop.dub"), new[]
        {
            "*trans-main:1000",
            "`" + octal,
        });
        string config = Path.Combine(root, "besm6.json");
        File.WriteAllText(config, "{\"checkLimit\":4}");

        int exitCode = new Besm6.Cli.CheckCommand().Execute(
            new[] { root, "--limit", "4", "--config", config });

        Assert.AreEqual(1, exitCode);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}
```

- [ ] **Step 2: Add a check regression proving control-card-only jobs are executed**

```csharp
[TestMethod]
public void Check_ControlCardOnlyJob_IsNotReportedAsNoContent()
{
    string root = Path.Combine(Path.GetTempPath(), "besm6_check_" + Guid.NewGuid().ToString("N"));
    string emptyTapes = Path.Combine(root, "empty-tapes");
    Directory.CreateDirectory(emptyTapes);
    try
    {
        File.WriteAllLines(Path.Combine(root, "name.dub"), new[] { "*name sample", "*end file" });
        string config = Path.Combine(root, "besm6.json");
        File.WriteAllText(config, "{\"tapes\":\"empty-tapes\",\"checkLimit\":4}");
        TextWriter original = Console.Out;
        var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            int exitCode = new Besm6.Cli.CheckCommand().Execute(new[] { root, "--config", config });
            Assert.AreEqual(1, exitCode);
            StringAssert.DoesNotContain(output.ToString(), "NO-CONTENT");
            StringAssert.Contains(output.ToString(), "MONSYS");
        }
        finally
        {
            Console.SetOut(original);
        }
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}
```

- [ ] **Step 3: Run both check tests and verify RED**

Run:

```powershell
dotnet test src/besm6.net/besm6.net.sln -c Release --filter "Name~Check_InstructionLimit|Name~Check_ControlCardOnlyJob"
```

Expected: the limit test returns 0, and the control-card-only job is labelled `NO-CONTENT` and returns 0.

- [ ] **Step 4: Remove the content shortcut and count limits as failures**

Replace the per-file body with unconditional parsing and execution:

```csharp
try
{
    DubJob job = JobParser.ParseFile(f);
    var loader = MachineFactory.CreateLoader(cfg);
    loader.InstructionLimit = limit;
    LoadResult result = loader.RunJob(job, File.ReadAllLines(f));
    if (result.LimitExceeded)
    {
        status = $"LIMIT(pc=0{result.Pc:X})";
        limitHit++;
    }
    else if (result.Success)
    {
        status = $"HALT({result.Instructions} instr)";
        passed++;
    }
    else
    {
        status = $"ERR:{result.ErrorMessage}";
        runFailed++;
    }
}
catch (FormatException ex)
{
    status = $"PARSE-ERR: {ex.Message}";
    parseFailed++;
}
catch (Exception ex)
{
    status = $"RUN-ERR: {ex.Message}";
    runFailed++;
}
```

Return failure when any non-halt result occurred:

```csharp
return (parseFailed == 0 && runFailed == 0 && limitHit == 0) ? 0 : 1;
```

- [ ] **Step 5: Run all CLI contract tests**

Run:

```powershell
dotnet test src/besm6.net/besm6.net.sln -c Release --filter "FullyQualifiedName~CliContractTests"
```

Expected: PASS.

- [ ] **Step 6: Commit honest batch checking**

```powershell
git add src/besm6.net/Cli/CheckCommand.cs src/besm6.net/tests/Besm6.Tests/CliContractTests.cs
git commit -m "Make batch checks fail on incomplete execution"
```

---

### Task 6: Root-checkout smoke verification and readiness documentation

**Files:**
- Modify: `plans/simulator-readiness-report.md`
- Verify only: `src/besm6.net/besm6.json`
- Verify only: `examples/name.dub`, `examples/algol.dub`, `examples/bemsh.dub`

**Interfaces:**
- Consumes: all behavior delivered by Tasks 1-5.
- Produces: an updated readiness report whose revision, test counts, working examples, and remaining CERN/device work match the current tree.

- [ ] **Step 1: Build and run the complete automated suites**

Run:

```powershell
dotnet build src/besm6.net/besm6.net.sln -c Release --no-restore
dotnet test src/besm6.net/besm6.net.sln -c Release --no-build --no-restore
py -3 -m unittest discover -s tools/tests -v
```

Expected: build has zero errors; MSTest has zero failures and the raw hello test is no longer skipped; all seven Python comparator tests pass. Existing analyzer warnings are recorded rather than misreported as new failures.

- [ ] **Step 2: Verify normal committed configuration from repository root**

Run:

```powershell
dotnet src/besm6.net/bin/Release/net8.0/besm6.dll help
dotnet src/besm6.net/bin/Release/net8.0/besm6.dll run examples/name.dub --limit 1000000 --no-wall-clock
dotnet src/besm6.net/bin/Release/net8.0/besm6.dll run examples/algol.dub --limit 10000000 --no-wall-clock
dotnet src/besm6.net/bin/Release/net8.0/besm6.dll run examples/bemsh.dub --limit 10000000 --no-wall-clock
```

Expected: help exits zero; each job reaches `Halted by STOP`; ALGOL contains `HELLO, WORLD!`; BEMSH contains `ПPИBETИK! ЭTO ABTOKOД БEMШ.`.

- [ ] **Step 3: Verify missing-resource diagnostics before execution**

Create a temporary config through PowerShell and run one job:

```powershell
$auditDir = Join-Path ([System.IO.Path]::GetTempPath()) ("besm6_missing_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $auditDir | Out-Null
Set-Content -LiteralPath (Join-Path $auditDir "besm6.json") -Value '{"tapes":"missing"}' -NoNewline
dotnet src/besm6.net/bin/Release/net8.0/besm6.dll run examples/name.dub --config (Join-Path $auditDir "besm6.json")
Remove-Item -LiteralPath $auditDir -Recurse
```

Expected: nonzero exit with a `Cannot mount MONSYS tape` diagnostic and no executed-instruction output.

- [ ] **Step 4: Update the readiness report with proven current state**

Revise `plans/simulator-readiness-report.md` to state:

```markdown
- Current revision and fresh MSTest/Python counts.
- Default root-checkout CLI resource discovery and fail-fast behavior are fixed.
- Raw hello/math/io execution-path bug is fixed and covered.
- ALGOL and BEMSH compile, execute, print expected text, and halt normally.
- Only two CERNLIB beacons are currently ported; the 420-test active matrix is the next project.
- Hardware-accurate asynchronous devices and interrupts remain a separate project.
- `check` reports execution termination, not semantic output equivalence.
```

Use exact measured counts and instruction totals from Step 1 and Step 2; do not copy stale values.

- [ ] **Step 5: Run diff and repository checks**

Run:

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; only the intended readiness-report edit is uncommitted.

- [ ] **Step 6: Commit verified readiness status**

```powershell
git add plans/simulator-readiness-report.md
git commit -m "Document reliable simulator execution status"
```

- [ ] **Step 7: Request final code review before integration**

Review scope: all commits created by Tasks 1-6 versus the design commit. Require explicit confirmation that resource resolution does not mask custom path mistakes, raw jobs never require MONSYS, and `check` cannot return zero for a limit or loader failure.
