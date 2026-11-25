using Besm6.Architecture.Machine;
using Besm6.Architecture.Numerics;
using Besm6.Architecture.Supervisor;
using Besm6.Processor.Memory;

namespace Besm6.Runtime;

public partial class MachineCore
{
    /// <summary>
    /// Flattens the implemented register model into stable names. Unimplemented
    /// special numbers (M18-M22, M24-M25, M30-M31) are intentionally absent:
    /// TO-8 §7.5 lists only 16, 17, 23, 26, 27, 28 and 29.
    /// </summary>
    private MachineRegisterView[] InspectRegisterMap(SupervisorSnapshot? supervisor)
    {
        var registers = new List<MachineRegisterView>(96);
        AddCpu(registers);
        AddSupervisor(registers, supervisor);
        AddAssignment(registers);
        AddProtection(registers);
        AddInterrupts(registers, supervisor);
        return registers.ToArray();
    }

    private void AddCpu(List<MachineRegisterView> registers)
    {
        registers.Add(new("K", "CPU", 15, Cpu.K, MachineRegisterAccess.ReadWrite));
        registers.Add(new("H", "CPU", 1, Cpu.RightInstruction ? 1UL : 0UL, MachineRegisterAccess.ReadWrite,
            Detail: "right half"));
        registers.Add(new("A", "CPU", 48, Cpu.A.Value, MachineRegisterAccess.ReadWrite));
        registers.Add(new("Y", "CPU", 48, Cpu.Y.Value, MachineRegisterAccess.ReadWrite));
        registers.Add(new("R", "CPU", 6, Cpu.R, MachineRegisterAccess.ReadWrite));
        registers.Add(new("C", "CPU", 15, Cpu.C, MachineRegisterAccess.ReadWrite, Alias: "M16"));
        registers.Add(new("ApplyC", "CPU", 1, Cpu.ApplyC ? 1UL : 0UL, MachineRegisterAccess.ReadWrite,
            Detail: "C application flag"));
        for (int index = 0; index < 16; index++)
            registers.Add(new($"M{index}", "Index", 15, Cpu.M[index], MachineRegisterAccess.ReadWrite));
    }

    private static void AddSupervisor(List<MachineRegisterView> registers, SupervisorSnapshot? supervisor)
    {
        if (supervisor is null) return;
        Add(0, "M16", 15, MachineRegisterAccess.ReadWrite, "20B", "alias of C");
        Add(1, "M17", 15, MachineRegisterAccess.WriteOnly, "21B", "control unit flags");
        Add(7, "M23", 15, MachineRegisterAccess.ReadWrite, "27B", "saved flags and mode");
        Add(10, "M26", 15, MachineRegisterAccess.ReadWrite, "32B", "extracode return");
        Add(11, "M27", 15, MachineRegisterAccess.ReadWrite, "33B", "interrupt return");
        // 15 address bits plus the assignment-mode flag retained at write time.
        Add(12, "M28", 16, MachineRegisterAccess.WriteOnly, "34B", "instruction comparison");
        Add(13, "M29", 16, MachineRegisterAccess.WriteOnly, "35B", "operand comparison");

        void Add(int index, string name, int width, MachineRegisterAccess access, string alias, string detail) =>
            registers.Add(new(name, "Supervisor", width, supervisor.SpecialRegisters[index], access,
                Alias: alias, Detail: detail));
    }

    private void AddAssignment(List<MachineRegisterView> registers)
    {
        if (MappedMemory is not { } mapped) return;
        var assignment = mapped.Assignment;
        int width = assignment.PhysicalPageCount > PageAssignment.PageCount ? 9 : 5;
        for (uint page = 0; page < PageAssignment.PageCount; page++)
        {
            uint group = page / 4;
            registers.Add(new($"RP{page}", "PageAssignment", width, assignment.GetPhysicalPage(page),
                MachineRegisterAccess.WriteOnly, GroupAddress("02", group), $"RPG{group}", $"field {page % 4}"));
        }
    }

    private void AddProtection(List<MachineRegisterView> registers)
    {
        if (MappedMemory is not { } mapped) return;
        for (uint group = 0; group < PageAssignment.ProtectionGroupCount; group++)
            registers.Add(new($"RZ{group}", "Protection", 8, mapped.Assignment.GetProtectionGroup(group),
                MachineRegisterAccess.WriteOnly, GroupAddress("03", group)));
    }

    private void AddInterrupts(List<MachineRegisterView> registers, SupervisorSnapshot? supervisor)
    {
        if (SupervisorIo is not { } io) return;
        ulong main = ((supervisor?.InternalInterrupts ?? 0) | io.ExternalInterrupts) & Word48.Mask48;
        registers.Add(new("РГПр", "Interrupts", 48, main, MachineRegisterAccess.ReadOnly,
            "0237", Detail: "main interrupt register"));
        registers.Add(new("РСП", "Interrupts", 24, io.PeripheralInterrupts & 0xFFFFFFu,
            MachineRegisterAccess.ReadOnly, Detail: "peripheral interrupt register"));
        registers.Add(new("РИ", "Interrupts", 48, io.ExternalInterruptMask & Word48.Mask48,
            MachineRegisterAccess.WriteOnly, "0036", Detail: "main mask register"));
        registers.Add(new("МСП", "Interrupts", 24, io.PeripheralInterruptMask & 0xFFFFFFu,
            MachineRegisterAccess.WriteOnly, Detail: "peripheral mask register"));
    }

    private static string GroupAddress(string prefix, uint group) =>
        prefix + Convert.ToString(group, 8);
}
