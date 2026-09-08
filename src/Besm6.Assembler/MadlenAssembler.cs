namespace Besm6.Assembler;

/// <summary>
/// Ассемблер строгого диалекта MADLEN: принимает английские мнемоники
/// и числовые формы, отклоняет русские мнемоники БЭМШ.
/// </summary>
public sealed class MadlenAssembler : IBesm6Assembler
{
    /// <inheritdoc/>
    public AssemblyDialect Dialect => AssemblyDialect.Madlen;

    /// <inheritdoc/>
    public ulong AssembleWord(string source)
        => AssemblyEngine.AssembleWord(source, AssemblyDialect.Madlen);

    /// <inheritdoc/>
    public AssemblyResult AssembleProgram(IEnumerable<string> source, int baseAddress = 512)
        => AssemblyEngine.AssembleProgram(source, baseAddress, AssemblyDialect.Madlen);
}