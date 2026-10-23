using Besm6.Processor.Memory;

namespace Besm6.Runtime;

public sealed record BufferRegisterView(string Name, int Slot, ulong Value, uint? Address, bool? Active, bool? Ready, bool? PendingWrite = null);
public sealed record DetailedMachineState(string MemoryModel, string ExecutionModel, BufferRegisterView[] Buffers,
    uint[] SpecialRegisters, ulong? ControlFlags, ulong? Interrupts, ulong? HardwareNanoseconds, uint[] ArithmeticCommands,
    uint[] PageAssignment, uint? OperandProtection);

public partial class MachineCore
{
    /// <summary>Independent diagnostic copies. Call at an execution boundary. Never creates a hardware model or touches recency.</summary>
    public DetailedMachineState InspectRegisters()
    {
        var buffers = new List<BufferRegisterView>();
        if (BufferedMemory is { } functional)
        {
            AddPhysical("БРЗ", functional.Buffers.GetOperandSnapshot());
            AddPhysical("БРС", functional.Buffers.GetInstructionSnapshot());
        }
        if (MappedMemory is { } mapped)
        {
            for (int slot = 0; slot < 8; slot++)
                buffers.Add(new("БРЗ", slot, mapped.ReadOperandBufferRegister(slot).Data.Value, null, null, null));
            AddAddressed("БАЗ/БРЗ (MRU)", mapped.GetOperandSnapshot());
            AddAddressed("БАС/БРС (MRU)", mapped.GetInstructionSnapshot());
        }
        if (ExistingHardwareModel is { } hardware)
            for (byte slot = 0; slot < 4; slot++)
            {
                var register = hardware.ReadRegisters.Inspect(slot);
                buffers.Add(new("БРЧ", slot, register.Word?.Data.Value ?? 0, null, register.Owner != 0, register.Ready));
            }
        var supervisor = Cpu.Supervisor?.Snapshot();
        return new(MemoryModel.ToString(), ExecutionModel.ToString(), buffers.ToArray(), supervisor?.SpecialRegisters ?? [],
            supervisor?.Status.EncodedControls, supervisor?.InternalInterrupts,
            ExistingHardwareModel?.Timeline.Now.Nanoseconds, ExistingHardwareModel?.InspectArithmeticCommands() ?? [],
            MappedMemory is { } mmu ? Enumerable.Range(0,32).Select(page => mmu.Assignment.GetPhysicalPage((uint)page)).ToArray() : [],
            MappedMemory?.Assignment.OperandProtectionMask);

        void AddPhysical(string name, MemoryBufferEntry[] entries)
        {
            for (int i = 0; i < entries.Length; i++) buffers.Add(new(name + " (MRU)", i, entries[i].Word.Data.Value,
                entries[i].PhysicalAddress, true, null, name == "БРЗ" ? entries[i].PendingWrite : null));
        }
        void AddAddressed(string name, AddressedMemoryBufferEntry[] entries)
        {
            for (int i = 0; i < entries.Length; i++) buffers.Add(new(name, i, entries[i].Word.Data.Value,
                entries[i].Request.EncodedValue, true, null));
        }
    }
}
