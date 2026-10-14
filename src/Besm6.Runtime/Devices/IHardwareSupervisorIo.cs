using Besm6.Architecture.Supervisor;

namespace Besm6.Runtime.Devices;

/// <summary>
/// Explicit identity of the device bus behind a supervisor I/O decorator.
/// The hardware driver verifies its shared MRAM port rather than assuming
/// that an arbitrary ISupervisorIo implementation performs timed DMA.
/// </summary>
public interface IHardwareSupervisorIo : ISupervisorIo
{
    SupervisorIoController HardwareController { get; }
}
