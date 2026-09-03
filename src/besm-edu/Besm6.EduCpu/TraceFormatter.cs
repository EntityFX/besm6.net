namespace Besm6.EduCpu;

/// <summary>
/// Форматтер трассы: строки с фиксированными колонками.
/// Не изменяет процессор. Машинные значения — в восьмеричной системе,
/// номер шага — в десятичной.
/// </summary>
public static class TraceFormatter
{
    public static string Format(Trace t)
    {
        string half = t.FromHalf == Half.Left ? "L" : "R";
        string next = $"0{Oct.Pad(t.NextAddress, 5)}{(t.NextHalf == Half.Left ? 'L' : 'R')}";
        return Pad(t.Step.ToString(), 5) + "  "
             + Pad($"0{Oct.Pad(t.FromAddress, 5)}", 6) + half + "  "
             + Pad(t.Disassembly, 16) + "  "
             + Pad("eff " + (t.EffectiveAddress != 0 ? $"0{Oct.Pad(t.EffectiveAddress, 5)}" : "----"), 9) + "  "
             + Pad(t.AccBefore.ToOctal(), 16) + " -> " + Pad(t.AccAfter.ToOctal(), 16) + "  "
             + next + "  " + t.Effect;
    }

    public static string Header()
        => Pad("ШАГ", 5) + "  "
         + Pad("АДРЕС", 6) + "  "
         + Pad("КОМАНДА", 16) + "  "
         + Pad("АДР.ИСП", 9) + "  "
         + Pad("ACC ДО", 16) + " -> " + Pad("ACC ПОСЛЕ", 16) + "  "
         + Pad("ДАЛЬШЕ", 7) + "  ЭФФЕКТ";

    private static string Pad(string s, int width) => s.Length >= width ? s : s + new string(' ', width - s.Length);
}