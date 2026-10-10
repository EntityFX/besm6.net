namespace Besm6.Core;

/// <summary>A captured admission; physical address and raw buffer word are frozen for this transfer.</summary>
internal sealed record MappedReadAdmission(MappedMemoryBackend Owner, MemoryRequestAddress Request,
    MemoryAccessKind Access, MemoryFaultSource Source, uint? PhysicalAddress,
    MemoryWord50? BufferedWord, ulong Epoch);
internal sealed record MappedWriteAdmission(MappedMemoryBackend Owner, AddressedMemoryBufferEntry Entry,
    uint PhysicalAddress, int Slot, ulong Version);

public sealed partial class MappedMemoryBackend
{
    private ulong _hardwareReadEpoch;
    /// <summary>Logical host barrier. False clears outstanding reads; true also abandons writeback leases.</summary>
    internal event Action<bool>? HardwareRequestsInvalidated;
    internal void GuestAssignmentChanging() => InvalidateHardwareRequests(false);
    private void InvalidateHardwareRequests(bool writes)
    {
        HardwareRequestsInvalidated?.Invoke(writes);
        _hardwareReadEpoch = unchecked(_hardwareReadEpoch + 1);
    }

    internal MappedReadAdmission AdmitHardwareRead(uint address, bool instruction, bool rightHalf)
    {
        MemoryRequestAddress request;
        ResolvedMemoryAddress resolved;
        MemoryAccessKind access;
        if (instruction)
        {
            request = new(address, Supervisor);
            access = rightHalf ? MemoryAccessKind.InstructionRight : MemoryAccessKind.InstructionLeft;
            try { resolved = Assignment.ResolveInstruction(address, Supervisor, rightHalf); }
            catch (MemoryProtectionException) { LastFault = new(request, access, MemoryFaultSource.Protection, null); throw; }
            InstructionAccessAdmitted?.Invoke(request, rightHalf);
            int index = _instructions.Find(request);
            if (index >= 0)
            {
                var hit = _instructions.At(index);
                return new(this, request, access, MemoryFaultSource.InstructionBuffer, hit.FetchAddress, hit.Word, _hardwareReadEpoch);
            }
        }
        else
        {
            request = new(address, AssignmentBlocked); access = MemoryAccessKind.OperandRead;
            resolved = AdmitOperand(request, false);
            if (resolved.Kind == MemoryAddressKind.ZeroOperand)
                return new(this, request, access, MemoryFaultSource.Panel, null,
                    MemoryWord50.Form(Word48.Zero, true, true), _hardwareReadEpoch);
            OperandAccessAdmitted?.Invoke(request, false);
            if (resolved.Kind != MemoryAddressKind.PanelRegister)
            {
                int index = _operands.Find(request);
                if (index >= 0)
                    return new(this, request, access, MemoryFaultSource.OperandBuffer, resolved.Address,
                        _operands.At(index).Word, _hardwareReadEpoch);
            }
        }
        bool panel = resolved.Kind == MemoryAddressKind.PanelRegister;
        return new(this, request, access, panel ? MemoryFaultSource.Panel : MemoryFaultSource.PhysicalMemory,
            resolved.Address, panel ? _panel[address] : null, _hardwareReadEpoch);
    }

    internal Word48 CompleteHardwareRead(MappedReadAdmission admission, MemoryWord50 word)
    {
        if (!ReferenceEquals(admission.Owner, this)) throw new InvalidOperationException("Foreign memory admission.");
        bool instruction = admission.Access != MemoryAccessKind.OperandRead;
        if (admission.Epoch == _hardwareReadEpoch)
        {
            var buffer = instruction ? _instructions : _operands;
            int index = buffer.Find(admission.Request);
            if (instruction && admission.Source != MemoryFaultSource.InstructionBuffer)
                buffer.Put(new(admission.Request, word, admission.PhysicalAddress), index);
            else if (index >= 0 && buffer.At(index).Word == word)
                buffer.Touch(index);
        }
        if (admission.PhysicalAddress is null) return Word48.Zero;
        if (!instruction)
            return CheckOperand(admission.Request, word, admission.PhysicalAddress.Value, admission.Source);
        bool right = admission.Access == MemoryAccessKind.InstructionRight;
        if (!word.HasValidInstructionControl(right))
        {
            LastFault = new(admission.Request, admission.Access, admission.Source, admission.PhysicalAddress);
            throw new MemoryControlException(admission.PhysicalAddress.Value, admission.Access);
        }
        return word.Data;
    }

    internal MappedWriteAdmission? CaptureOldestHardwareWrite()
    {
        if (_operands.Count == 0) return null;
        int index = _operands.Count - 1;
        var entry = _operands.At(index);
        int slot = _operands.SlotAt(index);
        return new(this, entry, Assignment.TranslateRequest(entry.Request), slot, _operands.VersionAt(slot));
    }

    internal bool CompleteHardwareWrite(MappedWriteAdmission admission)
    {
        if (!ReferenceEquals(admission.Owner, this)) throw new InvalidOperationException("Foreign memory writeback.");
        int index = _operands.Find(admission.Entry.Request);
        if (index < 0 || _operands.SlotAt(index) != admission.Slot ||
            _operands.VersionAt(admission.Slot) != admission.Version) return false;
        _operands.RemoveAt(index); // A new value in the same BRZ must survive an old completion.
        return true;
    }
}
