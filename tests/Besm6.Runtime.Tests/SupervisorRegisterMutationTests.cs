using Besm6.Architecture.Machine;
using Besm6.Architecture.Numerics;

namespace Besm6.Tests;

[TestClass]
public sealed class SupervisorRegisterMutationTests
{
    [TestMethod]
    public void EveryDocumentedRegisterChangesInSupervisorMode()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        var mmu = machine.MappedMemory!;
        for (uint page = 0; page < 32; page++) mmu.SetPhysicalPage(page, (page + 1) % 32);
        var cpu = machine.Cpu;
        var supervisor = cpu.Supervisor!;
        var io = machine.SupervisorIo!;
        var before = Snapshot(machine);

        // H changes when the CPU moves from the left to the right half.
        machine.Memory.Write(8, new(Assembler.Legacy.Assembler.Asm("atx 9, stop")));
        cpu.StartAt(8);
        cpu.Step();
        Assert.IsTrue(cpu.RightInstruction, "H did not change after a left-half instruction.");

        cpu.K = 0x123;
        cpu.A = new(0x123456789ABCUL);
        cpu.Y = new(0x0FEDCBA98765UL);
        cpu.R = 0x2A;
        cpu.ApplyC = true;
        for (int index = 1; index < 16; index++) cpu.SetM(index, (uint)(0x100 + index));

        supervisor.WriteModifier(16, 0x111); // C and its M16 alias
        supervisor.WriteModifier(17, 0x403); // M17
        supervisor.WriteModifier(23, 0x123); // M23
        supervisor.WriteModifier(26, 0x234); // M26
        supervisor.WriteModifier(27, 0x345); // M27
        supervisor.WriteModifier(28, 0x456); // M28 includes the assignment flag
        supervisor.WriteModifier(29, 0x567); // M29 includes the assignment flag

        // Hosted pages start at 1..32; writing 0..31 changes every RP.
        for (uint group = 0; group < 8; group++)
        {
            uint first = group * 4;
            ulong encoded = first | ((ulong)(first + 1) << 5) |
                ((ulong)(first + 2) << 10) | ((ulong)(first + 3) << 15);
            supervisor.WriteRegister(0x10 + group, new(encoded));
        }

        for (uint group = 0; group < 4; group++)
            supervisor.WriteRegister(0x18 + group, new(((ulong)(group + 1) * 0x11) << 20));

        supervisor.WriteRegister(0x1E, new(0x800)); // RI
        for (uint slot = 0; slot < 8; slot++) supervisor.WriteRegister(slot, new(0x100 + slot)); // BRZ

        supervisor.LatchInternalCause(1UL << 20); // РГПр internal cause
        io.RaiseInterrupts(1UL << 24);             // РГПр external cause
        io.ConfigureConsole(0);                    // РСП
        io.WriteDevice(28, new(0x200));            // МСП

        var after = Snapshot(machine);
        foreach (string name in ExpectedNames)
            Assert.AreNotEqual(before[name].Value, after[name].Value, $"{name} did not change.");

        // M0 is the logical zero, not a writable index register.
        Assert.AreEqual(0UL, after["M0"].Value);
    }

    [TestMethod]
    public void BufferRegistersChangeInSupervisorMode()
    {
        var machine = new MachineCore(ProcessorProfile.Supervisor);
        var supervisor = machine.Cpu.Supervisor!;
        for (int slot = 0; slot < 8; slot++) supervisor.WriteRegister((uint)slot, new((ulong)(0x100 + slot)));

        var buffers = machine.InspectRegisters().Buffers;
        for (int slot = 0; slot < 8; slot++)
            Assert.AreEqual((ulong)(0x100 + slot), buffers.Single(buffer =>
                buffer.Name == "БРЗ" && buffer.Slot == slot).Value);

        // A fetched instruction populates a BRS slot; the observation never fetches itself.
        machine.Memory.Write(8, new(Assembler.Legacy.Assembler.Asm("atx 9, stop")));
        machine.Cpu.StartAt(8);
        machine.Cpu.Step();
        Assert.IsTrue(machine.InspectRegisters().Buffers.Any(buffer =>
            buffer.Name == "БРС" && buffer.Slot == 0 && buffer.Value != 0));
    }

    private static readonly string[] ExpectedNames =
    [
        "K", "H", "A", "Y", "R", "C", "ApplyC", "M1", "M2", "M3", "M4", "M5",
        "M6", "M7", "M8", "M9", "M10", "M11", "M12", "M13", "M14", "M15",
        "M16", "M17", "M23", "M26", "M27", "M28", "M29",
        "RP0", "RP1", "RP2", "RP3", "RP4", "RP5", "RP6", "RP7",
        "RP8", "RP9", "RP10", "RP11", "RP12", "RP13", "RP14", "RP15",
        "RP16", "RP17", "RP18", "RP19", "RP20", "RP21", "RP22", "RP23",
        "RP24", "RP25", "RP26", "RP27", "RP28", "RP29", "RP30", "RP31",
        "RZ0", "RZ1", "RZ2", "RZ3",
        "РГПр", "РСП", "РИ", "МСП"
    ];

    private static Dictionary<string, MachineRegisterView> Snapshot(MachineCore machine) =>
        machine.InspectRegisters().Registers.ToDictionary(register => register.Name, register => register);
}
