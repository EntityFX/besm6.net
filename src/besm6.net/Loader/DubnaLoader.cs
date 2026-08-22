using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Besm6.Core;

namespace Besm6.Loader
{
    /// <summary>
    /// Загрузчик программ Dubna (.dub job-скрипты).
    /// Оркестрирует: разбор скрипта, загрузку в память (raw-слова), загрузку MONSYS,
    /// обработку экстракодов и запуск на процессоре.
    ///
    /// Основной рабочий путь — загрузка программ из raw-восьмеричных слов (.dub)
    /// и их выполнение на <see cref="Processor"/>.
    /// </summary>
    public sealed class DubnaLoader
    {
        public const int DefaultLoadBase = 512; // 01000 octal

        private readonly MachineCore _machine;
        private readonly ExtracodeHandler _extracode;

        // Смонтированные диски по каналу (unit 030..067).
        private readonly Dictionary<int, TapeImage> _disksByUnit = new();
        // Смонтированные диски по tape-id.
        private readonly Dictionary<long, TapeImage> _disksByTapeId = new();
        // Барабаны (unit 0..31). Барабан #1 — COSY-скрипт.
        private readonly Dictionary<int, TapeImage> _drumsByUnit = new();

        private readonly string? _tapesDir;

        /// <summary>Предел числа исполненных инструкций (защита от зависаний).</summary>
        public long InstructionLimit { get; set; } = 1_000_000_000;

        /// <summary>Выводить диагностику загрузки.</summary>
        public bool Verbose { get; set; }

        /// <summary>Аккумуляция вывода программ (для перехвата в тестах/CLI).</summary>
        public Action<string>? Output { get; set; }

        /// <summary>Трассировка инструкций (для отладки). null = выключена.</summary>
        public Action<int, long>? InstructionTrace { get; set; }

        /// <summary>
        /// Обработчик ввода с терминала (E71 flags=6).
        /// Получает prompt, возвращает строку ввода.
        /// </summary>
        public Func<string, string>? Input { get; set; }

        /// <summary>Количество исполненных инструкций за последний запуск.</summary>
        public long InstructionsExecuted { get; private set; }

        /// <summary>Признак остановки по команде СТОП.</summary>
        public bool HaltedByStop { get; private set; }

        public DubnaLoader(MachineCore machine, string? tapesDir = null)
        {
            _machine = machine;
            _tapesDir = tapesDir;
            _extracode = new ExtracodeHandler(
                machine,
                diskByTapeId: id => _disksByTapeId.TryGetValue(id, out var d) ? d : null,
                diskByUnit: u => _disksByUnit.TryGetValue(u, out var d) ? d : null,
                // Барабаны создаются на лету (порт drum_init): пустой барабан при первом обращении.
                drumByUnit: u =>
                {
                    if (!_drumsByUnit.TryGetValue(u, out var d))
                    {
                        d = new TapeImage(0, new byte[TapeImage.DrumNWords * 6], readOnly: false);
                        _drumsByUnit[u] = d;
                    }
                    return d;
                },
                output: s => (Output ?? (x => Console.Write(x)))(s),
                input: p => { if (Input != null) return Input(p); Console.Write(p); return Console.ReadLine() ?? ""; },
                mountTape: (tapeId, unit) => MountTape(unit, tapeId),
                findTape: tapeId => {
                    if (_disksByTapeId.TryGetValue(tapeId, out var disk))
                    {
                        // Найти unit по disk.
                        foreach (var kv in _disksByUnit)
                            if (ReferenceEquals(kv.Value, disk))
                                return kv.Key;
                    }
                    return 0;
                },
                    releaseTapes: mask => {
                    // Release tapes by bitmask.
                    for (int i = 0; i < 16; i++)
                    {
                        if ((mask & (1L << i)) != 0)
                        {
                            int unit = 24 + i;
                            _disksByUnit.Remove(unit);
                        }
                    }
                });

            // Барабан #1 — для COSY-скрипта.
            _drumsByUnit[1] = new TapeImage(0, new byte[TapeImage.DrumNWords * 6], readOnly: false);
        }

        //
        // ─── Загрузка лент ───────────────────────────────────────────────────
        //

        /// <summary>
        /// Смонтировать ленту по tape-id на заданный канал (unit).
        /// Пытается загрузить образ из dubna/tapes.
        /// </summary>
        public bool MountTape(int unit, long tapeId)
        {
            if (_disksByUnit.ContainsKey(unit))
                return true;

            var path = TapeImage.FindImagePath(tapeId, _tapesDir);
            if (path == null)
            {
                // Порт C++ disk_mount: ВСЕГДА создаёт Disk.
                // Если файл не найден — создаём пустой диск (нули).
                if (Verbose) Console.WriteLine($"Tape image for id 0x{tapeId:X12} not found, creating empty disk");
                // C++ disk_mount: встроенный диск = 288 зон (PAGE_NWORDS × 288).
                var empty = new TapeImage(tapeId, new byte[TapeImage.PageNWords * 6 * 288], readOnly: true);
                _disksByUnit[unit] = empty;
                _disksByTapeId[tapeId] = empty;
                return true;
            }

            var image = TapeImage.LoadFromFile(tapeId, path);
            _disksByUnit[unit] = image;
            _disksByTapeId[tapeId] = image;
            if (Verbose) Console.WriteLine($"Mounted {path} as disk 0{unit:X}");
            return true;
        }

        /// <summary>
        /// Смонтировать все ленты, упомянутые в *tape картах скрипта.
        /// </summary>
        public void MountScriptTapes(DubJob job)
        {
            foreach (var mount in job.TapeMounts)
            {
                long tapeId = TapeImage.TapeIdByName(mount.Name);
                if (tapeId == 0) continue;
                MountTape(24 + (mount.Channel & 0x1F), tapeId);
            }
            // MONSYS всегда на канале 030.
            if (!_disksByUnit.ContainsKey(24))
                MountTape(24, TapeImage.TapeMonsys);
        }

        //
        // ─── Барабан #1: COSY-скрипт ─────────────────────────────────────────
        //

        /// <summary>
        /// Записать job-скрипт на барабан #1 в формате COSY
        /// (порт Machine::load_script из dubna/machine.cpp).
        /// </summary>
        public void WriteScriptToDrum(DubJob job, IEnumerable<string> rawLines)
        {
            var drum = _drumsByUnit[1];
            int offset = 0;
            foreach (var line in rawLines)
            {
                string trimmed = line.TrimEnd('\r', '\n');
                if (trimmed.Length == 0)
                {
                    WriteCosyLine(drum, ref offset, "");
                    continue;
                }
                if (trimmed[0] == '`')
                {
                    long word = JobParser.ParseOctalWord(trimmed.Substring(1).Trim(), trimmed);
                    drum.WriteWord(offset++, word);
                }
                else
                {
                    // MONSYS не знает директиву *assem — транслируем в *madlen.
                    if (trimmed.StartsWith("*assem", StringComparison.OrdinalIgnoreCase))
                        trimmed = "*madlen" + trimmed.Substring(6);
                    // MONSYS не знает директиву *forex (FORTRAN-диалект) — транслируем в *fortran.
                    else if (trimmed.StartsWith("*forex", StringComparison.OrdinalIgnoreCase))
                        trimmed = "*fortran" + trimmed.Substring(6);
                    WriteCosyLine(drum, ref offset, trimmed);
                }
            }
            // Финальная карта '*end file'.
            WriteCosyLine(drum, ref offset, "*end file");
        }

        private static void WriteCosyLine(TapeImage drum, ref int offset, string text)
        {
            byte[] encoded = CosyCodec.EncodeCosy(CosyCodec.Utf8ToKoi7(text));
            for (int i = 0; i < encoded.Length; i += 6)
            {
                long word = 0;
                for (int b = 0; b < 6; b++)
                {
                    byte byteVal = (i + b < encoded.Length) ? encoded[i + b] : (byte)0;
                    word = (word << 8) | byteVal;
                }
                drum.WriteWord(offset++, word);
            }
        }

        //
        // ─── Загрузка и запуск ───────────────────────────────────────────────
        //

        /// <summary>
        /// Основная точка входа: загрузить .dub скрипт и запустить.
        /// Если скрипт состоит из raw-слов — выполняется минимальный путь.
        /// Иначе — попытка загрузки через MONSYS (boot_ms_dubna).
        /// </summary>
        public LoadResult RunScript(string path)
        {
            var job = JobParser.ParseFile(path);
            return RunJob(job, File.ReadAllLines(path));
        }

        /// <summary>
        /// Загрузить скрипт в память БЕЗ выполнения (для TUI/отладчика).
        /// Заполняет память (raw-слова) либо готовит MONSYS-загрузчик,
        /// устанавливает PC. Возвращает стартовый PC.
        /// </summary>
        public long LoadScript(string path)
        {
            var job = JobParser.ParseFile(path);
            var rawLines = File.ReadAllLines(path);
            _machine.Reset();
            MountScriptTapes(job);
            InstallExtracodeHook();

            if (job.RawWords.Count > 0)
            {
                int baseAddr = job.TransMain ?? DefaultLoadBase;
                for (int i = 0; i < job.RawWords.Count; i++)
                {
                    int addr = (baseAddr + i) & 0x7FFF;
                    _machine.Memory.Write(addr, new Word48(job.RawWords[i]));
                }
                _machine.Cpu.SetPc(baseAddr);
                _memStartBase = baseAddr;
                if (Verbose)
                    Console.WriteLine($"Loaded {job.RawWords.Count} raw words at 0{baseAddr:X}, start PC=0{baseAddr:X}");
                return baseAddr;
            }

            if (job.AssemProgram.Count > 0)
            {
                int baseAddr = job.TransMain ?? DefaultLoadBase;

                // Ассемблируем через ProgramAssembler.
                var textLines = job.AssemProgram
                    .Where(w => !w.IsRaw && !string.IsNullOrWhiteSpace(w.Text))
                    .Select(w => w.Text!)
                    .ToList();
                var rawValues = job.AssemProgram
                    .Where(w => w.IsRaw)
                    .Select(w => (index: job.AssemProgram.ToList().FindIndex(x => ReferenceEquals(x, w)), w.Value))
                    .ToList();

                var asmResult = Besm6.Asm.ProgramAssembler.Assemble(textLines, baseAddr);
                for (int i = 0; i < asmResult.Words.Count; i++)
                {
                    int addr = (baseAddr + i) & 0x7FFF;
                    _machine.Memory.Write(addr, new Word48(asmResult.Words[i]));
                }
                foreach (var (idx, val) in rawValues)
                {
                    int addr = (baseAddr + idx) & 0x7FFF;
                    _machine.Memory.Write(addr, new Word48(val));
                }

                _machine.Cpu.SetPc(baseAddr);
                _memStartBase = baseAddr;
                if (Verbose)
                    Console.WriteLine($"Assembled {asmResult.Words.Count} words at 0{baseAddr:X}, start PC=0{baseAddr:X}");
                return _memStartBase;
            }

            // MONSYS-путь (без запуска).
            WriteScriptToDrum(job, rawLines);
            MountScriptTapes(job);
            BootMsDubna();
            _memStartBase = (int)_machine.Cpu.GetPc();
            return _machine.Cpu.GetPc();
        }

        /// <summary>Базовый адрес загруженной программы (для окна памяти TUI).</summary>
        public int LoadedBase => _memStartBase;
        private int _memStartBase = 0;

        /// <summary>Выполнить уже загруженную программу (для TUI/отладчика).</summary>
        public LoadResult RunLoaded() => RunBounded();

        /// <summary>
        /// Загрузить и запустить job.
        /// </summary>
        public LoadResult RunJob(DubJob job, IEnumerable<string> rawLines)
        {
            _machine.Reset();
            MountScriptTapes(job);

            // Путь MONSYS: компиляция через ОС (MADLEN, BEMSH, ALGOL, FORTRAN, B).
            // Если есть *execute — ОС компилирует и исполняет программу.
            // Также: если секция *assem содержит MADLEN-формат (program:, данные) — нужен MONSYS.
            bool needsOs = job.Execute != null;

            if (!needsOs && job.RawWords.Count > 0)
            {
                // Минимальный путь: raw-слова прямо в память.
                return RunRawWords(job);
            }

            if (!needsOs && job.AssemProgram.Count > 0)
            {
                // Путь *assem без *execute: локальная ассемблерная сборка.
                // Работает для простых мнемоник (не для MADLEN/BEMSH исходников).
                return RunAssem(job);
            }

            // Основной путь: пишем скрипт на барабан #1, MONSYS компилирует/запускает.
            WriteScriptToDrum(job, rawLines);
            return BootAndRun(job);
        }

        /// <summary>
        /// Минимальный путь: загрузить raw-восьмеричные слова в память и выполнить.
        /// </summary>
        public LoadResult RunRawWords(DubJob job)
        {
            int baseAddr = job.TransMain ?? DefaultLoadBase;
            for (int i = 0; i < job.RawWords.Count; i++)
            {
                int addr = (baseAddr + i) & 0x7FFF;
                _machine.Memory.Write(addr, new Word48(job.RawWords[i]));
            }
            _machine.Cpu.SetPc(baseAddr);
            InstallExtracodeHook();

            if (Verbose)
                Console.WriteLine($"Loaded {job.RawWords.Count} raw words at 0{baseAddr:X}, start PC=0{baseAddr:X}");

            return RunBounded();
        }

        /// <summary>
        /// Путь *assem: ассемблировать мнемоники/сырые слова секции в память и выполнить.
        /// </summary>
        public LoadResult RunAssem(DubJob job)
        {
            int baseAddr = job.TransMain ?? DefaultLoadBase;

            // Разделить на сырые слова и мнемонические строки.
            // Если есть мнемоники — используем ProgramAssembler (2-pass, лейблы).
            var textLines = new List<string>();
            var rawValues = new List<(int index, long value)>();

            int wordIdx = 0;
            foreach (var w in job.AssemProgram)
            {
                if (w.IsRaw)
                {
                    // Сырое слово — записываем на фиксированном месте.
                    rawValues.Add((wordIdx, w.Value));
                }
                else if (!string.IsNullOrWhiteSpace(w.Text))
                {
                    textLines.Add(w.Text);
                }
                wordIdx++;
            }

            // Ассемблируем через ProgramAssembler (поддерживает лейблы, MADLEN, BEMSH).
            var asmResult = Besm6.Asm.ProgramAssembler.Assemble(textLines, baseAddr);

            // Записываем все слова в память.
            for (int i = 0; i < asmResult.Words.Count; i++)
            {
                int addr = (baseAddr + i) & 0x7FFF;
                _machine.Memory.Write(addr, new Word48(asmResult.Words[i]));
            }

            // Перезаписываем сырые слова в их позиции.
            foreach (var (idx, val) in rawValues)
            {
                int addr = (baseAddr + idx) & 0x7FFF;
                _machine.Memory.Write(addr, new Word48(val));
            }

            _machine.Cpu.SetPc(baseAddr);
            InstallExtracodeHook();

            if (Verbose)
                Console.WriteLine($"Assembled {asmResult.Words.Count} words at 0{baseAddr:X}, start PC=0{baseAddr:X}");

            return RunBounded();
        }

        /// <summary>
        /// MONSYS-путь: boot_ms_dubna (данные + загрузчик), затем запуск.
        /// </summary>
        public LoadResult BootAndRun(DubJob job)
        {
            // Убеждаемся, что MONSYS смонтирован на канале 030.
            MountScriptTapes(job);
            BootMsDubna();
            InstallExtracodeHook();

            if (Verbose)
                Console.WriteLine("Booting MONSYS from disk 030 (drum 021 -> disk 030)...");

            return RunBounded();
        }

        private void InstallExtracodeHook()
        {
            _machine.Cpu.ExtracodeHandler = _extracode.Handle;
        }

        private LoadResult RunBounded()
        {
            long limit = InstructionLimit;
            InstructionsExecuted = 0;
            HaltedByStop = false;
            long lastReport = 0;

            // Подключаем трассировку.
            if (InstructionTrace != null)
            {
                long[] counter = { 0 };
                _machine.StepTrace = (pc, word) =>
                {
                    counter[0]++;
                    InstructionTrace(pc, word);
                };
            }

            while (InstructionsExecuted < limit)
            {
                try
                {
                    bool stopped = _machine.Step();
                    InstructionsExecuted++;
                    if (stopped)
                    {
                        HaltedByStop = true;
                        return LoadResult.Halt(_machine.Cpu.GetPc(), InstructionsExecuted);
                    }

                    if (Verbose && InstructionsExecuted - lastReport >= 100_000)
                    {
                        lastReport = InstructionsExecuted;
                        long pc = _machine.Cpu.GetPc();
                        Console.Write($"\r  [{InstructionsExecuted / 1000}K] PC=0{pc:X4}   ");
                    }
                }
                catch (ProcessorException ex)
                {
                    // Точный порт C++ machine.cpp run() catch (lines 131-149):
                    // 1) stack_correction()
                    // 2) пустое сообщение → чистый halt (E74)
                    // 3) intercept() → goto again (продолжить)
                    // 4) иначе → fail
                    _machine.Cpu.StackCorrection();

                    if (string.IsNullOrEmpty(ex.Message))
                    {
                        // E74: clean halt (equivalent to C++ empty message check).
                        HaltedByStop = true;
                        return LoadResult.Halt(_machine.Cpu.GetPc(), InstructionsExecuted);
                    }

                    if (_machine.Cpu.Intercept(ex.Message))
                    {
                        // Intercept applied — resume from intercept address.
                        if (Verbose)
                            Console.Write($"\r  [INTERCEPT @ 0{_machine.Cpu.GetPc():X4}] {ex.Message} → 0{_machine.Cpu.GetPc():X4}\n");
                        continue;
                    }

                    // Not intercepted — fatal error.
                    if (Verbose) Console.WriteLine();
                    return LoadResult.Failed(ex.Message, _machine.Cpu.GetPc(), InstructionsExecuted);
                }
            }
            if (Verbose) Console.WriteLine();
            return LoadResult.StoppedByLimit(_machine.Cpu.GetPc(), InstructionsExecuted);
        }

        //
        // ─── Загрузчик MONSYS (порт boot_ms_dubna) ───────────────────────────
        //

        /// <summary>
        /// Подготовить загрузчик MONSYS: данные таблиц 03000-03010 и стартовый код.
        /// Точный порт Machine::boot_ms_dubna из dubna/machine.cpp (930-975).
        /// Магический код по М. Попову для запуска статического загрузчика.
        /// </summary>
        public void BootMsDubna()
        {
            var mem = _machine.Memory;
            var asm = Besm6.Asm.Assembler.Asm;

            // Физический обмен: барабан 021 перенаправляем на диск 030 (MONSYS).
            MountTape(24, TapeImage.TapeMonsys);
            if (_disksByUnit.TryGetValue(24, out var monsysDisk))
                _extracode.MapDrumToDisk(17, 24, monsysDisk);   // 021 oct -> 030 oct

            //
            // Магический код (Mikhail Popov, STARTJOB routine):
            //
            // Адреса: 02010(oct)=1032(dec), 03000(oct)=1536(dec), 03010(oct)=1544(dec)
            mem.Write(1032, new Word48(asm("vtm -5(1),     *70 3002")));   // читаем ТРП для загрузчика
            mem.Write(1033, new Word48(asm("xta 377,       atx 3010")));   // берём тракт MONITOR*+/MONTRAN
            mem.Write(1034, new Word48(asm("xta 363,       atx 100")));    // восстановим испорченный IОLISТ*
            mem.Write(1035, new Word48(asm("vtm 53401(17), utc")));        // магазин
            mem.Write(1036, new Word48(asm("*70 3010(1),   utc")));        // каталоги
            mem.Write(1037, new Word48(asm("vlm 2014(1),   ita 17")));     // aload по адресу 716b
            mem.Write(1038, new Word48(asm("atx 716,       *70 717")));    // infloa по адресу 717b — статический загрузчик
            mem.Write(1039, new Word48(asm("xta 17,        ati 16")));     //
            mem.Write(1040, new Word48(asm("atx 2(16),     arx 3001")));   // прибавляем 10b
            mem.Write(1041, new Word48(asm("atx 17,        xta 3000")));   // 'INPUTCAL'
            mem.Write(1042, new Word48(asm("atx (16),      vtm 1673(15)"))); // call CHEKJOB*
            mem.Write(1043, new Word48(asm("uj (17),       utc")));        // в статический загрузчик

            // Данные таблицы (03000-03010 oct = 1536-1544 dec).
            mem.Write(1536, new Word48(183533445462124L));                // 05156606564434154 oct = 'INPUTCAL' in Text encoding
            mem.Write(1537, new Word48(8L));                              // 0000000000000010 oct = прибавляем 10b
            mem.Write(1538, new Word48(141562122145921L));               // 04014000000210201 oct = инициатор
            mem.Write(1539, new Word48(65536L));                          // 0000000000200000 oct = таблица резидентных программ
            mem.Write(1540, new Word48(824633790471L));                  // 00014000000210007 oct = каталоги
            mem.Write(1541, new Word48(69632L));                          // 0000000000210000 oct = временной
            mem.Write(1542, new Word48(824633790472L));                  // 00014000000210010 oct = библиотеки
            mem.Write(1543, new Word48(69633L));                          // 0000000000210001 oct = (физ. и мат.)
            mem.Write(1544, new Word48(824633790493L));                  // 00014000000210035 oct = /MONTRAN

            _machine.Cpu.SetPc(1032);
        }
    }

    /// <summary>
    /// Результат загрузки/выполнения.
    /// </summary>
    public sealed class LoadResult
    {
        public bool Success { get; private init; }
        public bool Stopped { get; private init; }
        public long Pc { get; private init; }
        public long Instructions { get; private init; }
        public string? ErrorMessage { get; private init; }
        public bool LimitExceeded { get; private init; }

        public static LoadResult Halt(long pc, long instr) => new()
        { Success = true, Stopped = true, Pc = pc, Instructions = instr };
        public static LoadResult StoppedByLimit(long pc, long instr) => new()
        { Success = false, Stopped = false, Pc = pc, Instructions = instr, LimitExceeded = true };
        public static LoadResult Failed(string msg, long pc, long instr) => new()
        { Success = false, Stopped = false, Pc = pc, Instructions = instr, ErrorMessage = msg };

        public override string ToString()
        {
            if (Stopped) return $"Halted by STOP at 0{Pc:X} after {Instructions} instructions";
            if (LimitExceeded) return $"Instruction limit exceeded at 0{Pc:X} after {Instructions} instructions";
            return $"Error at 0{Pc:X}: {ErrorMessage}";
        }

        private LoadResult() { }
    }
}