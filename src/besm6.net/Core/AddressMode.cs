namespace Besm6.Core
{
    /// <summary>
    /// Режим адресации.
    /// </summary>
    public enum AddressMode
    {
        Direct,       // addr + M[reg]
        Indirect,     // addr + M[reg] + MOD
        Stack,        // addr==0 && reg==15 → M[15]--
    }
}
