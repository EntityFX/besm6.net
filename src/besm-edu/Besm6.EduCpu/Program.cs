namespace Besm6.EduCpu;

/// <summary>
/// Консольный фронт демонстратора — «внешний пайплайн»:
/// разбор аргументов → DemoProgram.Load → new Cpu → листинг → исполнение → трасса → дамп → итог (всё через Cpu).
/// Аргументы: --step, --max-steps N, --dump START END, --help.
/// </summary>
public static class Program
{
    private static int Main(string[] args)
    {
        bool step = false;
        int? maxSteps = null;
        ushort? dumpStart = null, dumpEnd = null;

        try
        {
            ParseArgs(args, ref step, ref maxSteps, ref dumpStart, ref dumpEnd);
        }
        catch (ArgumentException ex)
        {
            Error(ex.Message);
            return 1;
        }

        Memory mem = new();
        (ushort entry, string expected) = DemoProgram.Load(mem);
        Cpu cpu = new(mem, entry);

        Console.WriteLine("Учебный процессор БЭСМ-6 — демонстратор выполнения одной программы");
        Console.WriteLine($"Вход: 0{Oct.Pad(entry, 5)}L    Ожидание: {expected}");
        Console.WriteLine();
        PrintListing(mem, entry);

        // Исполнение: пошагово или до STOP; каждый шаг складывает запись трассы.
        List<Trace> traces = new();
        try
        {
            if (step)
            {
                RunStepped(cpu, maxSteps, traces);
            }
            else
            {
                int limit = maxSteps ?? int.MaxValue;
                for (int i = 0; i < limit; ++i)
                {
                    if (cpu.Stopped)
                    {
                        break;
                    }

                    traces.Add(cpu.Step());
                }

                if (!cpu.Stopped)
                {
                    throw new StepLimitExceededException(cpu.Steps, limit);
                }
            }
        }
        catch (CpuException ex)
        {
            Error(ex.Message);
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("ТРАССА");
        Console.WriteLine(TraceFormatter.Header());
        foreach (Trace t in traces)
        {
            Console.WriteLine(TraceFormatter.Format(t));
        }

        if (dumpStart is { } dStart && dumpEnd is { } dEnd)
        {
            Console.WriteLine();
            Console.WriteLine(MemoryDump.Format(mem, dStart, dEnd));
        }

        // Итог: проверка результата в ячейке 0110 и печать сводки.
        Word48 result = mem.Read(72); // 0110
        Console.WriteLine();
        Console.WriteLine("ИТОГ");
        Console.WriteLine("Причина останова : STOP выполнен");
        Console.WriteLine($"Число шагов     : {cpu.Steps}");
        Console.WriteLine($"Ячейка 0110     : {result.ToOctal()} (ожидалось 00000000000000014)");
        return 0;
    }

    private static void RunStepped(Cpu cpu, int? maxSteps, List<Trace> traces)
    {
        int limit = maxSteps ?? int.MaxValue;
        while (traces.Count < limit)
        {
            if (cpu.Stopped)
            {
                return;
            }

            Console.WriteLine();
            Console.Write("Enter — выполнить следующую команду: ");
            Console.ReadLine();
            traces.Add(cpu.Step());
        }

        if (!cpu.Stopped)
        {
            throw new StepLimitExceededException(cpu.Steps, limit);
        }
    }

    private static void PrintListing(Memory mem, ushort entry)
    {
        Console.WriteLine("ЛИСТИНГ КОМАНД");
        for (ushort addr = entry; addr <= 12; addr = (ushort)(addr + 1)) // 010..014
        {
            PrintWord(mem, addr);
        }

        PrintWord(mem, 16); // 020: успешная ветвь

        Console.WriteLine();
        Console.WriteLine("ДАННЫЕ");
        for (ushort addr = 64; addr <= 73; addr = (ushort)(addr + 1)) // 100..111
        {
            Console.WriteLine($"0{Oct.Pad(addr, 5)}  {mem.Read(addr).ToOctal()}");
        }
    }

    private static void PrintWord(Memory mem, ushort addr)
    {
        Word48 word = mem.Read(addr);
        Console.WriteLine($"0{Oct.Pad(addr, 5)}  L: {DisasmSafe(word.LeftHalf)}   R: {DisasmSafe(word.RightHalf)}");
    }

    private static string DisasmSafe(uint raw24)
    {
        if (raw24 == 0)
        {
            return "--";
        }

        try
        {
            return Instruction.Decode(raw24).Disassembly;
        }
        catch (CpuException)
        {
            return $"*{Oct.Pad(raw24, 6)}";
        }
    }

    private static void ParseArgs(string[] args, ref bool step, ref int? maxSteps, ref ushort? dumpStart, ref ushort? dumpEnd)
    {
        for (int i = 0; i < args.Length; ++i)
        {
            switch (args[i])
            {
                case "--help":
                    PrintHelp();
                    Environment.Exit(0);
                    break;

                case "--step":
                    step = true;
                    break;

                case "--max-steps":
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException("--max-steps требует положительного целого значения.");
                    }

                    if (!int.TryParse(args[++i], out int n) || n <= 0)
                    {
                        throw new ArgumentException("--max-steps требует положительного целого значения.");
                    }

                    maxSteps = n;
                    break;

                case "--dump":
                    if (i + 2 >= args.Length || !Oct.TryParse(args[i + 1], out ushort d1) || !Oct.TryParse(args[i + 2], out ushort d2) || d2 < d1)
                    {
                        throw new ArgumentException("--dump START END: два восьмеричных адреса, END >= START (включительно).");
                    }

                    i += 2; (dumpStart, dumpEnd) = (d1, d2);
                    break;

                default:
                    throw new ArgumentException($"Неизвестный аргумент: {args[i]}. Используйте --help.");
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Использование: Besm6.EduCpu [--step] [--max-steps N] [--dump START END] [--help]");
        Console.WriteLine();
        Console.WriteLine("  (без аргументов)  выполнить встроенную программу целиком;");
        Console.WriteLine("  --step            ждать Enter перед каждой командой;");
        Console.WriteLine("  --max-steps N     ограничить число шагов (N > 0);");
        Console.WriteLine("  --dump START END  после выполнения показать дамп памяти по диапазону (восьмерично, включительно);");
        Console.WriteLine("  --help            эта справка (без запуска процессора).");
    }

    private static void Error(string message)
    {
        Console.Error.WriteLine("Ошибка: " + message);
    }
}
