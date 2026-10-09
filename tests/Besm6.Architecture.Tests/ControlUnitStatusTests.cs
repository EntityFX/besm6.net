using Besm6.Architecture;

namespace Besm6.Tests;

[TestClass]
public sealed class ControlUnitStatusTests
{
    // Printed bit positions in TO-8, sheet 135, §7.7; bit 1 is least significant.
    [TestMethod]
    [DataRow(ControlUnitFlags.AssignmentBlocked, 1)]
    [DataRow(ControlUnitFlags.ProtectionBlocked, 2)]
    [DataRow(ControlUnitFlags.StopOnInternalInterrupt, 3)]
    [DataRow(ControlUnitFlags.StopOnControlInterrupt, 4)]
    [DataRow(ControlUnitFlags.MatchWriteAddress, 5)]
    [DataRow(ControlUnitFlags.ExternalInterruptsBlocked, 11)]
    [DataRow(ControlUnitFlags.AutomaticB, 12)]
    public void WiredFlagsOccupyPrintedPositions(ControlUnitFlags flag, int position)
    {
        Assert.AreEqual(1u << (position - 1), new ControlUnitStatus(flag).EncodedControls);
    }

    // Independent literal vectors from the PA/SA00 mask in §7.8, not CPU results.
    [TestMethod]
    [DataRow(0x000u, 0x001u, 0x001u)]
    [DataRow(0x000u, 0x002u, 0x002u)]
    [DataRow(0x000u, 0x400u, 0x400u)]
    [DataRow(0xC1Fu, 0x000u, 0x81Cu)]
    [DataRow(0x81Cu, 0x403u, 0xC1Fu)]
    [DataRow(0x81Cu, 0x243Fu, 0xC1Fu)]
    [DataRow(0x015u, 0x002u, 0x016u)]
    public void SupervisorAddressControlsPreserveOtherWiredFlags(uint initial, uint address, uint expected)
    {
        var state = new ControlUnitStatus((ControlUnitFlags)initial);
        Assert.AreEqual(expected, state.WithSupervisorAddressControls(address).EncodedControls);
        Assert.AreEqual(initial, state.EncodedControls);
    }

    [TestMethod]
    [DataRow(0x20u)]
    [DataRow(0x200u)]
    [DataRow(uint.MaxValue)]
    public void UndefinedHostFlagsAreRejected(uint flags)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ControlUnitStatus((ControlUnitFlags)flags));
    }

    [TestMethod]
    public void SnapshotDoesNotInventColdResetOrAliasTheStackRegister()
    {
        Assert.AreEqual(0u, default(ControlUnitStatus).EncodedControls);
        Assert.AreEqual("21", Convert.ToString(ControlUnitStatus.RegisterNumber, 8));
        Assert.AreEqual("6037", Convert.ToString(ControlUnitStatus.KnownMask, 8));
        Assert.AreEqual("2003", Convert.ToString(ControlUnitStatus.AddressControlMask, 8));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => default(ControlUnitStatus).WithSupervisorAddressControls(32768));
    }
}
