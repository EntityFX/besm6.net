namespace Besm6.Runtime.Extracodes;
internal sealed partial class HostedClockService
{
    internal ulong DateWord()
    {
        //   если machine.is_entropy_enabled() — реальное текущее местное
        //   время (localtime), иначе фиксированная дата для тестов.
        // только -r отключает её (ref/main.cpp:193-196).
        // Раскладка union E50_Date_Time (ref/extracode.h):
        //   decisec  b0-3,  sec_lo  b4-7,  sec_hi  b8-11, min_lo  b12-15,
        //   min_hi   b16-19, hour_lo b20-23, hour_hi b24-25 (2 бита),
        //   year_lo  b26-29, year_hi b30-33, month_lo b34-37, month_hi b38-41,
        //   day_lo   b42-45, day_hi  b46-47 (2 бита)
        ulong word;
        if (UseWallClock)
        {
            var now = DateTime.Now;
            word = (ulong)((now.Day / 10) & 0x3) << 46 | (ulong)(now.Day % 10) << 42 | (ulong)((now.Month / 10) & 0xF) << 38 | (ulong)(now.Month % 10) << 34 | (ulong)(((now.Year % 100) / 10) & 0xF) << 30 | (ulong)((now.Year % 100) % 10) << 26 | (ulong)((now.Hour / 10) & 0x3) << 24 | (ulong)(now.Hour % 10) << 20 | (ulong)((now.Minute / 10) & 0xF) << 16 | (ulong)(now.Minute % 10) << 12 | (ulong)((now.Second / 10) & 0xF) << 8 | (ulong)(now.Second % 10) << 4;
        }
        else
        {
            //   day_hi=0, day_lo=4   → 04
            //   month_hi=0, month_lo=7 → July (ИЮЛ)
            //   year_hi=2, year_lo=4  → 2024
            //   hour_hi=2, hour_lo=3  → 23
            //   min_hi=4, min_lo=5    → 45
            //   sec_hi=5, sec_lo=6    → 56
            //   decisec=0
            word = (4UL << 42) // day_lo=4
 | (7UL << 34) // month_lo=7
 | (2UL << 30) | (4UL << 26) // year_hi=2, year_lo=4
 | (2UL << 24) | (3UL << 20) // hour_hi=2, hour_lo=3
 | (4UL << 16) | (5UL << 12) // min_hi=4, min_lo=5
 | (5UL << 8) | (6UL << 4); // sec_hi=5, sec_lo=6
        }

        return word;
    }
}
