namespace Besm6.Architecture
{
    /// <summary>
    /// Декодированное 24-битное полуслово команды БЭСМ-6.
    /// </summary>
    /// <param name="Register">Номер индексного регистра M0–M15.</param>
    /// <param name="Opcode">Код операции без битов расширения адреса.</param>
    /// <param name="Address">Нормализованный 15-битный исходный адрес.</param>
    /// <param name="Format">Короткий или длинный формат команды.</param>
    public readonly record struct DecodedInstruction(
        byte Register,
        Opcode Opcode,
        ushort Address,
        InstructionFormat Format);
}
