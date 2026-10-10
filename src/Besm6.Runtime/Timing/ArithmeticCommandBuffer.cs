namespace Besm6.Runtime.Timing;

/// <summary>
/// БАК — буфер арифметических команд.
/// Four 17-bit arithmetic command registers, TO-3 edition 1-65, §§3.3–3.6,
/// sheets31–33. A slot is released by operand acceptance (PVR), not by
/// arithmetic completion (IZOP). No delay or opcode mapping is inferred here.
/// This component is not attached to the functional processor yet.
/// </summary>
public sealed class ArithmeticCommandBuffer
{
    public const int Capacity = 4;
    public const uint CommandMask = 0x1FFFF;
    private readonly uint[] _commands = new uint[Capacity];
    private int _receive;
    private int _issue;
    public int Count { get; private set; }
    public bool CanReceive => Count < Capacity;
    public int ReceiveRegister => _receive;
    public int IssueRegister => _issue;

    /// <summary>Command admission; false leaves a full buffer unchanged.</summary>
    public bool TryReceive(uint command)
    {
        if ((command & ~CommandMask) != 0)
            throw new ArgumentOutOfRangeException(nameof(command), "Arithmetic buffer commands have 17 bits.");
        if (!CanReceive) return false;
        _commands[_receive] = command;
        _receive = (_receive + 1) % Capacity;
        Count++;
        return true;
    }

    /// <summary>PRB observation; examining the issued code does not free its register.</summary>
    public bool TryPeek(out uint command)
    {
        command = Count == 0 ? 0 : _commands[_issue];
        return Count != 0;
    }

    /// <summary>
    /// The caller asserts that PVR has actually occurred. Operand readiness and
    /// RPK permission are separate prerequisites supplied by the arithmetic unit model.
    /// Returns the accepted code and advances the issue ring exactly once.
    /// </summary>
    public uint AcceptOperand()
    {
        if (!TryPeek(out uint command))
            throw new InvalidOperationException("PVR requires a command in the selected arithmetic command register.");
        _commands[_issue] = 0;
        _issue = (_issue + 1) % Capacity;
        Count--;
        return command;
    }

    /// <summary>Copies logical issue order, excluding empty registers.</summary>
    public uint[] Snapshot()
    {
        var result = new uint[Count];
        for (int i = 0; i < result.Length; i++) result[i] = _commands[(_issue + i) % Capacity];
        return result;
    }
}
