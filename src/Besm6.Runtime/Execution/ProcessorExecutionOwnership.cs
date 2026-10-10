using Besm6.Runtime.Modeling;

namespace Besm6.Runtime.Execution;

/// <summary>
/// Connects execution owners of one shared CPU. Neither owner knows the other's
/// implementation: only this machine-level coordinator connects their guards.
/// It does not execute instructions, convert clock units or synchronize registers.
/// Pending hardware commands additionally hold the CPU's prepared-command lease.
/// This is reentrancy protection for a single-threaded machine, not a thread lock.
/// </summary>
internal sealed class ProcessorExecutionOwnership
{
    private readonly Processor _cpu;
    private readonly FunctionalProcessorSimulation _simulation;
    private HardwareProcessorModel? _model;

    internal ProcessorExecutionOwnership(Processor cpu, FunctionalProcessorSimulation simulation)
    {
        _cpu = cpu;
        _simulation = simulation;
        simulation.Scheduler.AdvanceStateChanged = SimulationAdvancementChanged;
    }

    internal void Attach(HardwareProcessorModel model)
    {
        if (_model is not null)
            throw new InvalidOperationException("This machine already has a hardware execution owner.");
        _model = model;
        // Lazy creation inside a functional callback must inherit its active guard.
        model.Timeline.AdvancementProhibited = _simulation.Scheduler.IsAdvancing;
        model.Timeline.AdvanceStateChanged = HardwareAdvancementChanged;
        model.ExecutionStateChanged = HardwareExecutionChanged;
    }

    private void SimulationAdvancementChanged(bool active)
    {
        _cpu.ExecutionProhibited = active;
        if (_model is { } model) model.Timeline.AdvancementProhibited = active;
    }

    private void HardwareAdvancementChanged(bool active)
    {
        _cpu.ExecutionProhibited = active;
        ProhibitSimulation(active || _model!.IsDriving);
    }

    private void HardwareExecutionChanged(bool active) =>
        ProhibitSimulation(active || _model!.Timeline.IsAdvancing);

    private void ProhibitSimulation(bool active)
    {
        _simulation.Scheduler.AdvancementProhibited = active;
        _simulation.Clock.AdvancementProhibited = active;
    }
}
