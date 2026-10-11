using Besm6.Runtime.Timing;

namespace Besm6.Runtime.Modeling;

/// <summary>
/// BRCh (БРЧ — буферные регистры чисел): four addressed transfer registers,
/// paired with the four BAK slots, TO-3 §3.7, sheets 36–37. These registers
/// hold an admitted raw reply until PVR; they are not an operand cache.
/// </summary>
internal sealed class ArithmeticReadRegisters
{
    internal const int Capacity = ArithmeticCommandBuffer.Capacity;
    private readonly HardwareBufferedRead?[] _words = new HardwareBufferedRead?[Capacity];
    private readonly ulong[] _owners = new ulong[Capacity];
    private readonly bool[] _ready = new bool[Capacity];

    internal void Reserve(byte register, ulong owner)
    {
        Validate(register);
        if (owner == 0 || _owners[register] != 0)
            throw new InvalidOperationException("The arithmetic read register is already reserved or its owner is invalid.");
        _owners[register] = owner;
        _ready[register] = false;
    }

    internal void Receive(byte register, ulong owner, HardwareBufferedRead reply)
    {
        EnsureOwner(register, owner);
        if (_ready[register]) throw new InvalidOperationException("An arithmetic read register already contains its reply.");
        _words[register] = reply;
        _ready[register] = true; // GBRCh — готовность БРЧ.
    }

    internal HardwareBufferedRead Accept(byte register, ulong owner)
    {
        EnsureOwner(register, owner);
        if (!_ready[register]) throw new InvalidOperationException("PVR requires a ready arithmetic read register.");
        var word = _words[register]!.Value;
        _ready[register] = false;
        _owners[register] = 0;
        return word;
    }

    internal void Release(byte register, ulong owner)
    {
        Validate(register);
        if (_owners[register] != owner || owner == 0) return;
        _ready[register] = false;
        _owners[register] = 0;
    }

    internal (MemoryWord50? Word, bool Ready, ulong Owner) Inspect(byte register)
    {
        Validate(register);
        return (_words[register]?.RawWord, _ready[register], _owners[register]);
    }

    private void EnsureOwner(byte register, ulong owner)
    {
        Validate(register);
        if (owner == 0 || _owners[register] != owner)
            throw new InvalidOperationException("A stale arithmetic transfer cannot modify this read register.");
    }

    private static void Validate(byte register)
    {
        if (register >= Capacity) throw new ArgumentOutOfRangeException(nameof(register));
    }
}
