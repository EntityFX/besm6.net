namespace Besm6.Runtime.Execution;

/// <summary>
/// Functional processor simulation: immediate shared CPU semantics, instruction
/// ticks, hosted diagnostics and batches. It does not own physical AU stages or
/// advance their nanosecond calendar. MachineCore owns the common resources.
/// </summary>
internal sealed class FunctionalProcessorSimulation
{
    private readonly Processor Cpu;
    private readonly IMemory Memory;
    private readonly SimulationClock _clock = new();
    private readonly EventScheduler _scheduler;
    private const ulong TicksPerInstruction = MachineCore.TicksPerInstruction;
    internal SimulationClock Clock => _clock;
    internal EventScheduler Scheduler => _scheduler;

    internal FunctionalProcessorSimulation(Processor cpu, IMemory memory)
    {
        Cpu = cpu;
        Memory = memory;
        _scheduler = new(_clock);
    }

    /// <summary>Хук трассировки: вызывается после каждой инструкции. null = трассировка выключена.</summary>
    public Action<int, ulong>? StepTrace { get; set; }

    /// <summary>
    /// Хук трассировки ИЗМЕНЕНИЙ регистров после каждого шага — точный аналог
    /// регистра ("A", "Y", "M0".."M17" в восьмеричной записи, "R", "C"
    /// или "CLEARC") и его
    /// значением. Печатает только изменённые регистры (сравнение с prev-состоянием),
    /// </summary>
    public Action<string, ulong>? RegisterTrace { get; set; }

    private bool _rtActive;
    private ulong _rtA, _rtY, _rtR;
    private uint _rtC;
    private readonly uint[] _rtM = new uint[16];
    private bool _rtApplyC;

    /// <summary>Зафиксировать текущее состояние как базу сравнения (вызывать до цикла шагов).</summary>
    public void BeginRegisterTrace()
    {
        _rtActive = true;
        _rtA = Cpu.GetA().Value;
        _rtY = Cpu.GetY().Value;
        _rtR = Cpu.GetR();
        _rtC = Cpu.C;
        _rtApplyC = Cpu.ApplyC;
        for (int index = 0; index < 16; index++)
        _rtM[index] = Cpu.GetM(index);
    }

    private void EmitRegisterTrace()
    {
        Action<string, ulong>? sink = RegisterTrace;
        if (sink is null)
        return;
        if (!_rtActive)
        {
            BeginRegisterTrace();
            return;
        }

        ulong a = Cpu.GetA().Value;
        ulong y = Cpu.GetY().Value;
        uint r = Cpu.GetR();
        uint c = Cpu.C;
        bool applyC = Cpu.ApplyC;
        if (a != _rtA) sink("A", a);
        if (y != _rtY) sink("Y", y);
        for (int index = 0; index < 16; index++)
        {
            uint value = Cpu.GetM(index);
            if (value != _rtM[index])
            sink("M" + Convert.ToString(index, 8), value);
        }
        if (r != _rtR) sink("R", r);
        if (applyC != _rtApplyC) sink(applyC ? "C" : "CLEARC", c);

        _rtA = a;
        _rtY = y;
        _rtR = r;
        _rtC = c;
        _rtApplyC = applyC;
        for (int index = 0; index < 16; index++)
        _rtM[index] = Cpu.GetM(index);
    }

    public bool Step()
    {
        long completed = 0;
        return Step(ref completed);
    }

    internal bool Step(ref long completed)
    {
        EnsureExecutionAllowed();
        DeliverCurrentEvents();
        bool stopped = Cpu.Step();
        if (!Cpu.LastStepCompleted) return stopped;
        // B1: одна выполненная инструкция = TicksPerInstruction тиков модельного времени.
        // Не влияет на наблюдаемую семантику уровня A (только учёт модельного времени).
        _clock.Advance(TicksPerInstruction);
        completed++;
        if (StepTrace != null)
        {
            int k = (int)Cpu.GetK();
            StepTrace(k, Memory.Read((uint)k).Value);
        }
        if (RegisterTrace is not null) EmitRegisterTrace();
        DeliverCurrentEvents();
        return stopped;
    }

    internal void EnsureExecutionAllowed()
    {
        if (Cpu.ExecutionProhibited)
        throw new InvalidOperationException("Machine execution inside a scheduler callback is not allowed.");
    }

    private void DeliverCurrentEvents()
    {
        if (_scheduler.NextEventTick is ulong next && next <= _clock.Tick)
        _scheduler.DeliverCurrentEvents();
    }

    /// <summary>
    /// Executes a bounded batch through the same Step path. The counter advances
    /// only after a successful step, including STOP; exceptions leave it exact.
    /// </summary>
    internal bool ExecuteBlock(int count, ref long instructionsExecuted)
    {
        EnsureExecutionAllowed();
        long end = instructionsExecuted + count;
        while (instructionsExecuted < end)
        {
            DeliverCurrentEvents();
            if (StepTrace is null && RegisterTrace is null && Cpu.CanExecuteUnobservedBlock)
            {
                int batch = (int)(end - instructionsExecuted);
                if (_scheduler.NextEventTick is ulong next)
                batch = (int)Math.Min((ulong)batch, next - _clock.Tick);
                long batchEnd = instructionsExecuted + batch;
                bool stopped = Cpu.ExecuteUnobservedBlock(batch,
                ref instructionsExecuted, ref _clock.TickReference);
                DeliverCurrentEvents();
                if (stopped) return true;
                if (instructionsExecuted == end) return false;
                if (instructionsExecuted == batchEnd) continue;
            }
            // Events may have installed hooks or modified the next instruction.
            // Recheck at the top after a completed batch; an extracode returns
            // without reaching its requested boundary and needs the ordinary step.
            if (Step(ref instructionsExecuted)) return true;
        }
        return false;
    }

}
