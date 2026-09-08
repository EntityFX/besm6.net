namespace Besm6.Assembler;

/// <summary>
/// Ассемблер с автоматическим определением диалекта: принимает и английские,
/// и русские мнемоники, сохраняя совместимое поведение прежнего ProgramAssembler.
/// </summary>
public sealed class AutoDetectingAssembler : IBesm6Assembler
{
    /// <inheritdoc/>
    public AssemblyDialect Dialect => AssemblyDialect.Auto;

    /// <inheritdoc/>
    public ulong AssembleWord(string source)
        => AssemblyEngine.AssembleWord(source, AssemblyDialect.Auto);

    /// <inheritdoc/>
    public AssemblyResult AssembleProgram(IEnumerable<string> source, int baseAddress = 512)
        => AssemblyEngine.AssembleProgram(source, baseAddress, AssemblyDialect.Auto);
}