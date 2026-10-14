using Besm6.Architecture.Isa;
using Besm6.Architecture.Machine;
using Besm6.Runtime.Execution;

namespace Besm6.Runtime.Diagnostics;

/// <summary>Owns only the observers installed by one execution run. Callback-installed hooks survive disposal.</summary>
internal sealed class ExecutionDiagnosticsSession(MachineCore machine, ExecutionLoop loop) : IDisposable
{
    private readonly MachineCore _machine = machine;
    private readonly ExecutionLoop _loop = loop;
    private Action<Opcode>? _counter, _observer;
    private bool _instructionObserversAttached;
    private CanonicalTraceWriter? _canonicalTraceWriter;
    private DiagnosticTraceWriter? _diagnosticTraceWriter;
    private Action<InstructionTraceRecord>? _attachedTypedInstructionTrace;
    private Action<int, ulong>? _attachedInstructionTrace;
    private Action<uint, bool, uint, uint>? _attachedCppInstructionTrace;
    private Action<string, ulong>? _attachedRegisterTrace;


    internal void AttachInstructionObservers(Action<Opcode>? counter, Action<Opcode>? observer)
    {
        _counter = counter;
        _observer = observer;
        _machine.Cpu.InstructionExecuted += counter;
        _machine.Cpu.InstructionExecuted += observer;
        _instructionObserversAttached = true;
    }

    internal void AttachRuntimeObservers()
    {
        if (_loop.InstructionTrace != null)
        {
            _machine.StepTrace += _loop.InstructionTrace;
            _attachedInstructionTrace = _loop.InstructionTrace;
        }

        if (_loop.CppInstructionTrace != null)
        {
            _machine.Cpu.TraceInstruction += _loop.CppInstructionTrace;
            _attachedCppInstructionTrace = _loop.CppInstructionTrace;
        }

        if (_loop.RegisterTrace != null)
        {
            _machine.BeginRegisterTrace();
            _machine.RegisterTrace += _loop.RegisterTrace;
            _attachedRegisterTrace = _loop.RegisterTrace;
        }

    }

    internal void DetachExecutionObservers()
    {
        if (_instructionObserversAttached)
        {
            _machine.Cpu.InstructionExecuted -= _counter;
            _machine.Cpu.InstructionExecuted -= _observer;
            _instructionObserversAttached = false;
        }
        if (_attachedInstructionTrace is not null) _machine.StepTrace -= _attachedInstructionTrace;
        if (_attachedCppInstructionTrace is not null) _machine.Cpu.TraceInstruction -= _attachedCppInstructionTrace;
        if (_attachedRegisterTrace is not null) _machine.RegisterTrace -= _attachedRegisterTrace;
        _attachedInstructionTrace = null;
        _attachedCppInstructionTrace = null;
        _attachedRegisterTrace = null;
    }

    internal void AttachFileTraceWriters()
    {
        string? canonicalPath = Environment.GetEnvironmentVariable("BESM6_CANON_TRACE");
        if (!string.IsNullOrWhiteSpace(canonicalPath))
        {
            ulong limit = ulong.MaxValue;
            string? value = Environment.GetEnvironmentVariable("BESM6_CANON_TRACE_LIMIT");
            if (ulong.TryParse(value, out ulong parsedLimit))
                limit = parsedLimit;

            _canonicalTraceWriter = new CanonicalTraceWriter(canonicalPath, limit);
            _machine.Cpu.InstructionTrace += _canonicalTraceWriter.Write;
        }

        if (_loop.TypedInstructionTrace is not null)
        {
            // РџСЂРѕС„Р°Р№Р»РµСЂ РѕРїРєРѕРґРѕРІ (Runtime/Profiling): С‚РѕР»СЊРєРѕ РЅР°Р±Р»СЋРґРµРЅРёРµ, РЅРµ РІР»РёСЏРµС‚ РЅР° СЃРµРјР°РЅС‚РёРєСѓ.
            _machine.Cpu.InstructionTrace += _loop.TypedInstructionTrace;
            _attachedTypedInstructionTrace = _loop.TypedInstructionTrace;
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BESM6_INSTR_TRACE")))
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "instr_trace.log");
            _diagnosticTraceWriter = new DiagnosticTraceWriter(path);
            _machine.Cpu.InstructionTrace += _diagnosticTraceWriter.Write;
            _machine.Cpu.RegisterTrace += _diagnosticTraceWriter.Write;
        }
    }

    private void DetachFileTraceWriters()
    {
        if (_attachedTypedInstructionTrace is not null)
            _machine.Cpu.InstructionTrace -= _attachedTypedInstructionTrace;
        _attachedTypedInstructionTrace = null;
        var canonical = _canonicalTraceWriter;
        var diagnostic = _diagnosticTraceWriter;
        _canonicalTraceWriter = null;
        _diagnosticTraceWriter = null;
        if (canonical is not null) _machine.Cpu.InstructionTrace -= canonical.Write;
        if (diagnostic is not null)
        {
            _machine.Cpu.InstructionTrace -= diagnostic.Write;
            _machine.Cpu.RegisterTrace -= diagnostic.Write;
        }
        // All subscriptions are removed even if closing either writer fails.
        try { canonical?.Dispose(); }
        finally { diagnostic?.Dispose(); }
    }

    public void Dispose()
    {
        DetachExecutionObservers();
        DetachFileTraceWriters();
    }
}
