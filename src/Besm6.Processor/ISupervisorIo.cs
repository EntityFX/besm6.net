namespace Besm6.Core;

/// <summary>Guest supervisor register and peripheral exchanges.</summary>
public interface ISupervisorIo
{
    Word48 ReadRegister(uint register);
    void WriteRegister(uint register, Word48 value);
    Word48 ReadDevice(uint address);
    void WriteDevice(uint address, Word48 value);
    ulong PendingExternalInterrupts { get; }
}
