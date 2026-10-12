using System.Text.Json;
using System.Text.Json.Serialization;
using Besm6.Runtime;

namespace Besm6.Cli;

/// <summary>Run one standalone supervisor machine through its selected execution owner.</summary>
public sealed class MachineCommand : ICommand
{
    public string Name => "machine";
    public string Description => "Run a standalone supervisor machine with functional or hardware execution";
    public string Usage => "besm6 machine <machine.json> [--execution functional|hardware] [--speed original|max|virtual] [--limit N] [--stats]";

    public int Execute(string[] args)
    {
        if (args.Length == 0) { Console.Error.WriteLine(Usage); return 1; }
        try
        {
            MachineExecutionModel? execution = null; ExecutionSpeed? speed = null; long? limit = null; bool stats = false;
            for (int i = 1; i < args.Length; i++)
            {
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
                switch (args[i])
                {
                    case "--execution": execution = MachineJobConfiguration.ParseExecutionModel(Value()); break;
                    case "--speed":
                        if (!ExecutionSpeedNames.TryParse(Value(), out var parsed))
                            throw new ArgumentException("--speed requires 'original' or 'max' or 'virtual'.");
                        speed = parsed; break;
                    case "--limit":
                        if (!long.TryParse(Value(), out var count) || count < 1) throw new ArgumentException("--limit requires a positive integer.");
                        limit = count; break;
                    case "--stats": stats = true; break;
                    default: throw new ArgumentException($"Unknown machine option: {args[i]}.");
                }
            }
            var configuration = MachineJobConfiguration.Load(args[0]);
            var selectedSpeed = speed ?? configuration.ParseSpeed();
            var machine = configuration.CreateMachine(execution);
            var consoleBytes = new List<object>(); var serialBytes = new List<object>();
            var printerLines = new List<object>();
            machine.SupervisorIo!.ConsoleOutput += (unit, value) => consoleBytes.Add(new { unit, value });
            machine.SupervisorIo.SerialOutput += (unit, value) => serialBytes.Add(new { unit, value });
            machine.SupervisorIo.PrinterOutput += (unit, rows) =>
                printerLines.Add(new { unit, rows = rows.Select(row => row.ToArray()).ToArray() });
            var result = machine.RunInstructions(limit ?? configuration.InstructionLimit, selectedSpeed, stats);
            var state = machine.Cpu;
            var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            json.Converters.Add(new JsonStringEnumConverter());
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                executionModel = machine.ExecutionModel, speed = selectedSpeed,
                result.Outcome.Success, result.Outcome.Stopped, result.Outcome.LimitExceeded,
                result.Outcome.Instructions, result.Outcome.ErrorMessage,
                state = new { k = state.GetK(), a = state.GetA().Value, y = state.GetY().Value, r = state.GetR(),
                    c = state.C, rightHalf = state.OnRightInstruction, m = Enumerable.Range(0, 16).Select(state.GetM).ToArray() },
                functionalTicks = machine.Clock.Tick,
                hardwareNanoseconds = machine.ExecutionModel == MachineExecutionModel.Hardware ? machine.HardwareTimeline.Now.Nanoseconds : (ulong?)null,
                statistics = result.Statistics,
                deviceOutput = new { consoleBytes, serialBytes, printerLines }
            }, json));
            return result.Outcome.Success ? 0 : 1;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or JsonException or IOException or OverflowException or NotSupportedException or Besm6.Core.ProcessorException)
        {
            Console.Error.WriteLine(error.Message); return 1;
        }
    }
}
