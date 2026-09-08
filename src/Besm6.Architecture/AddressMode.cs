namespace Besm6.Architecture
{
    /// <summary>
    /// Режим адресации.
    /// </summary>
    public enum AddressMode
    {
        Direct,       // addr + M[reg]
        Indirect,     // addr + M[reg] + C
        Stack,        // addr==0 && reg==15 → M[15]--
    }
}
