using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Besm6.Runtime.Configuration;
using Besm6.Runtime.Loading;
using Besm6.Runtime.Machine;

// Read-only experimental instrumentation, deliberately separate from the runtime.
// "unpaced" attaches the exact original timing implementation to a Max execution.
// Generated scaling jobs must not read guest elapsed-time services in this mode.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string job = Path.GetFullPath(args[0]);
string destination = Path.GetFullPath(args[1]);
string assetsDirectory = Path.GetFullPath(args[2]);
bool unpaced = args.Length < 4 || args[3] == "unpaced";
double maximumModelSeconds = args.Length > 4 ? double.Parse(args[4], CultureInfo.InvariantCulture) : 120;
var cfg = Config.Load(Path.Combine(assetsDirectory, "besm6.json"));
cfg.Speed = unpaced ? ExecutionSpeed.Max : ExecutionSpeed.Original;
cfg.GuestClock = !unpaced;
cfg.UseWallClock = false;
cfg.DefaultLimit = 1_000_000_000;
var assets = MachineFactory.ValidateRuntimeAssets(cfg);
var machine = MachineFactory.CreateMachine(cfg);
var loader = MachineFactory.CreateLoader(cfg, machine, assets);
loader.CollectStatistics = true;
loader.UseWallClock = false;
loader.StepAttemptLimit = 1_000_000_000;
loader.WallClockLimitMs = 240_000;
loader.LoadScript(job);

var runtime = typeof(DubnaLoader).Assembly;
Type clockType = runtime.GetType("Besm6.Runtime.Timing.HostedOriginalTiming", throwOnError: true)!;
object execution = typeof(DubnaLoader).GetField("_exec", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(loader)!;
FieldInfo clockField = execution.GetType().GetField("_originalTiming", BindingFlags.Instance | BindingFlags.NonPublic)!;
MethodInfo readSeconds = clockType.GetProperty("Seconds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetMethod!;
object? shadowClock = unpaced ? Activator.CreateInstance(clockType, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
    binder: null, args: [machine.Cpu.State, machine.Cpu, null], culture: null) : null;
Func<double>? clockGetter = shadowClock is null ? null : (Func<double>)readSeconds.CreateDelegate(typeof(Func<double>), shadowClock);
double Clock()
{
    if (clockGetter is not null) return clockGetter();
    object? clock = clockField.GetValue(execution);
    if (clock is null) return loader.Statistics?.ModelSeconds ?? 0;
    clockGetter = (Func<double>)readSeconds.CreateDelegate(typeof(Func<double>), clock);
    return clockGetter();
}
long completed = 0;
var points = new List<Point> { new("start", 0, 0) };
var pending = new Queue<string>();
var text = new StringBuilder();
bool executeSeen = false, entered = false, firstPrint = false, stopSeen = false;
uint? printAddress = null, stopAddress = null, freeAddress = null;
void Mark(string name) => points.Add(new(name, completed, Clock()));
loader.Output = chunk =>
{
    text.Append(chunk);
    string trimmed = chunk.Trim();
    if (trimmed.StartsWith("*FORTRAN", StringComparison.OrdinalIgnoreCase)) pending.Enqueue("fortran_card");
    if (trimmed.StartsWith("*EXECUTE", StringComparison.OrdinalIgnoreCase)) pending.Enqueue("execute_card");
    if (!executeSeen) return;
    var match = Regex.Match(chunk, @"\bBCDWRIT\*\s+([0-7]{5})\b");
    if (match.Success) printAddress = Convert.ToUInt32(match.Groups[1].Value, 8);
    match = Regex.Match(chunk, @"\bSTOP\*\s+([0-7]{5})\b");
    if (match.Success) stopAddress = Convert.ToUInt32(match.Groups[1].Value, 8);
    match = Regex.Match(chunk, @"\bCBO\S*\s+([0-7]{5})\b");
    if (match.Success) freeAddress = Convert.ToUInt32(match.Groups[1].Value, 8);
};
loader.InstructionExecuted = _ =>
{
    completed++;
    while (pending.Count > 0)
    {
        string name = pending.Dequeue();
        Mark(name);
        if (name == "execute_card") executeSeen = true;
    }
};
loader.ExecutionCheckpoint = _ =>
{
    if ((completed & 16383) == 0 && Clock() > maximumModelSeconds)
        throw new InvalidOperationException("Experimental model-time budget exceeded");
    if (!executeSeen || machine.Cpu.State.IsRightHalf) return;
    uint k = machine.Cpu.K;
    if (!entered && k == 512) { Mark("program_entry"); entered = true; }
    if (entered && !firstPrint && printAddress is uint printer && k == printer)
    { Mark("first_formatted_output"); firstPrint = true; }
    if (entered && !stopSeen && stopAddress is uint stop && k == stop)
    { Mark("stop_library"); stopSeen = true; }
};
LoadResult? outcome = null;
string? error = null;
try { outcome = loader.RunLoaded(); }
catch (Exception exception) { error = exception.Message; }
double modelSeconds = Clock();
(shadowClock as IDisposable)?.Dispose();
var statistics = loader.Statistics;
points.Add(new("halt", completed, modelSeconds));
using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
byte[] bytes = new byte[6];
for (uint address = 0; address < machine.Memory.Size; address++)
{
    ulong word = machine.Memory.Read(address).Value;
    for (int i = 0; i < 6; i++) bytes[i] = (byte)(word >> (40 - i * 8));
    hash.AppendData(bytes);
}
var entry = points.FirstOrDefault(p => p.Name == "program_entry");
var print = points.FirstOrDefault(p => p.Name == "first_formatted_output");
var result = new
{
    job, sourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(job))),
    executionMode = unpaced ? "max_with_original_timing_observer" : "original",
    guestElapsedTimeServicesEnabled = !unpaced,
    success = outcome?.Success == true && error is null,
    error = error ?? outcome?.ErrorMessage,
    outcome = outcome?.ToString(),
    originalModelSeconds = modelSeconds,
    beforeProgramSeconds = entry?.Seconds,
    computeSeconds = entry is not null && print is not null ? (double?)(print.Seconds - entry.Seconds) : null,
    computeInstructions = entry is not null && print is not null ? (long?)(print.Instructions - entry.Instructions) : null,
    computeFraction = entry is not null && print is not null ? (double?)((print.Seconds - entry.Seconds) / modelSeconds) : null,
    completedInstructions = completed,
    observerCountMatchesStatistics = completed == statistics?.CompletedInstructions,
    freeAddressOctal = freeAddress is uint free ? Convert.ToString(free, 8) : null,
    allocatedUserWordsFromLinkMap = freeAddress is uint end ? (long?)end - 512 : null,
    memorySha256 = Convert.ToHexString(hash.GetHashAndReset()),
    boundaries = points,
    hostElapsedSeconds = statistics?.ElapsedSeconds,
    output = text.ToString()
};
Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
File.WriteAllText(destination, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
Console.WriteLine($"{Path.GetFileName(job)} success={result.success} model={modelSeconds:F9} compute={result.computeSeconds:F9} instructions={completed}");
return result.success ? 0 : 1;

record Point(string Name, long Instructions, double Seconds);
