namespace Besm6.Assembler;

/// <summary>
/// Ассемблер строгого диалекта БЭМШ: принимает русские мнемоники и директивы,
/// отклоняет английские мнемоники MADLEN.
/// </summary>
public sealed class BemshAssembler : IBesm6Assembler
{
    /// <inheritdoc/>
    public AssemblyDialect Dialect => AssemblyDialect.Bemsh;

    /// <inheritdoc/>
    public ulong AssembleWord(string source)
        => AssemblyEngine.AssembleWord(source, AssemblyDialect.Bemsh);

    /// <inheritdoc/>
    public AssemblyResult AssembleProgram(IEnumerable<string> source, int baseAddress = 512)
        => AssemblyEngine.AssembleProgram(source, baseAddress, AssemblyDialect.Bemsh);
}