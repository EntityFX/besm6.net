namespace Besm6.EduCpu;

/// <summary>
/// Встроенная учебная программа: демонстрирует оба формата команд,
/// индексную адресацию, условный и безусловный переходы.
/// Заполняет память командами и данными, возвращает адрес входа
/// и описание ожидаемого результата.
/// </summary>
public static class DemoProgram
{
    private const ushort VtmAddr = 8;   // 010: vtm 1(1) | xta 100(1)
    private const ushort ArxAddr = 9;   // 011: arx 102 | atx 110
    private const ushort AexAddr = 10;  // 012: aex 110 | uza 020
    private const ushort UjAddr = 11;   // 013: uj 014  (недостижимая ветвь)
    private const ushort ErrAddr = 12;  // 014: atx 111 | uj 014
    private const ushort OkAddr = 16;   // 020: xta 110 | stop
    private const ushort DataBase = 64; // 100: данные 100, 101, 102
    private const ushort ResultAddr = 72; // 110
    private const ushort ErrorAddr = 73;  // 111

    public static (ushort Entry, string Expected) Load(Memory mem)
    {
        mem.Write(DataBase, Word48.FromOctal("3"));
        mem.Write((ushort)(DataBase + 1), Word48.FromOctal("5"));   // читаемый элемент
        mem.Write((ushort)(DataBase + 2), Word48.FromOctal("7"));   // складываемый элемент
        mem.Write(ResultAddr, Word48.FromOctal("0"));
        mem.Write(ErrorAddr, Word48.FromOctal("0"));

        // Шаг 1: VTM загружает M1 = 1; XTA с M1 читает второй элемент данных.
        Put(mem, VtmAddr, Half.Left, Instruction.EncodeLong(Op.Vtm, 1, 1));
        Put(mem, VtmAddr, Half.Right, Instruction.EncodeShort(Op.Xta, 1, DataBase));

        // Шаг 2: ARX циклически складывает ещё одно слово; ATX сохраняет результат.
        Put(mem, ArxAddr, Half.Left, Instruction.EncodeShort(Op.Arx, 0, (ushort)(DataBase + 2)));
        Put(mem, ArxAddr, Half.Right, Instruction.EncodeShort(Op.Atx, 0, ResultAddr));

        // Шаг 3: AEX сравнивает ACC с сохранённым (получаем 0); UZA идёт в успех.
        Put(mem, AexAddr, Half.Left, Instruction.EncodeShort(Op.Aex, 0, ResultAddr));
        Put(mem, AexAddr, Half.Right, Instruction.EncodeLong(Op.Uza, 0, OkAddr));

        // Недостижимая ветвь ошибки: UJ показывает безусловный переход в листинге.
        Put(mem, UjAddr, Half.Left, Instruction.EncodeLong(Op.Uj, 0, ErrAddr));
        Put(mem, ErrAddr, Half.Left, Instruction.EncodeShort(Op.Atx, 0, ErrorAddr));
        Put(mem, ErrAddr, Half.Right, Instruction.EncodeLong(Op.Uj, 0, ErrAddr));

        // Успешная ветвь: загружает результат и останавливается.
        Put(mem, OkAddr, Half.Left, Instruction.EncodeShort(Op.Xta, 0, ResultAddr));
        Put(mem, OkAddr, Half.Right, Instruction.EncodeLong(Op.Stop, 0, 0));

        return (VtmAddr, "Ячейка 0110 должна содержать 14 (восьмерично, 5 + 7); ACC = 14.");
    }

    private static void Put(Memory mem, ushort addr, Half half, uint raw24)
    {
        Word48 word = mem.Read(addr);
        Word48 packed = half == Half.Left
            ? Word48.Pack(raw24, word.RightHalf)
            : Word48.Pack(word.LeftHalf, raw24);
        mem.Write(addr, packed);
    }
}
