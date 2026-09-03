namespace Besm6.EduCpu;

/// <summary>
/// Оперативная память: ровно 32 768 (2^15) 48-разрядных слов.
/// Аппаратный аналог — ячеечная оперативная память БЭСМ-6 (без временной модели банков).
/// </summary>
public sealed class Memory
{
    public const int WordCount = 1 << 15;
    public const ushort MaxAddress = WordCount - 1; // 077777

    private readonly Word48[] _words = new Word48[WordCount];

    /// <summary>Этап «выборка»: чтение слова по 15-разрядному адресу (вне 0..077777 — ошибка).</summary>
    public Word48 Read(ushort address)
    {
        CheckAddress(address);
        return _words[address];
    }

    /// <summary>Этап «запись»: сохранение слова по 15-разрядному адресу.</summary>
    public void Write(ushort address, Word48 value)
    {
        CheckAddress(address);
        _words[address] = value;
    }

    private static void CheckAddress(ushort address)
    {
        if (address > MaxAddress)
        {
            throw new OutOfRangeAddressException($"Адрес 0{Oct.Pad(address, 5)} вне памяти (0..077777).");
        }
    }
}