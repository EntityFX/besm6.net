namespace Besm6.EduCpu;

/// <summary>Форматирование участка памяти (дамп): заголовок + по строке на адрес, включительный диапазон.</summary>
public static class MemoryDump
{
    public static string Format(Memory mem, ushort start, ushort end)
    {
        if (end < start)
        {
            throw new ArgumentException("Конец диапазона меньше начала.", nameof(end));
        }
        var lines = new List<string> { "ДАМП ПАМЯТИ" };
        for (int a = start; a <= end; ++a)
        {
            lines.Add($"0{Oct.Pad((ulong)a, 5)}  {mem.Read((ushort)a).ToOctal()}");
        }
        return string.Join("\n", lines);
    }
}