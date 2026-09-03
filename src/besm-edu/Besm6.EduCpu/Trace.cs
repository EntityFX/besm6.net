namespace Besm6.EduCpu;

/// <summary>
/// Неизменяемая запись трассы одного шага — результат прохода пайплайна Step().
/// Аппаратный аналог — отладочный вывод (tracing) узлов процессора.
/// </summary>
public readonly struct Trace
{
    public int Step { get; }
    public ushort FromAddress { get; }
    public Half FromHalf { get; }
    public uint Raw24 { get; }
    public Op Opcode { get; }
    public byte Register { get; }
    public ushort BaseAddress { get; }
    public string Disassembly { get; }
    public ushort EffectiveAddress { get; }
    public Word48 AccBefore { get; }
    public Word48 AccAfter { get; }
    public ushort NextAddress { get; }
    public Half NextHalf { get; }
    public string Effect { get; }

    public Trace(int step, ushort fromAddress, Half fromHalf, uint raw24, Instruction ins,
        ushort effectiveAddress, Word48 accBefore, Word48 accAfter, ushort nextAddress, Half nextHalf, string effect)
    {
        Step = step;
        FromAddress = fromAddress;
        FromHalf = fromHalf;
        Raw24 = raw24;
        Opcode = ins.Opcode;
        Register = ins.Register;
        BaseAddress = ins.BaseAddress;
        Disassembly = ins.Disassembly;
        EffectiveAddress = effectiveAddress;
        AccBefore = accBefore;
        AccAfter = accAfter;
        NextAddress = nextAddress;
        NextHalf = nextHalf;
        Effect = effect;
    }
}