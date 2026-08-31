using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Besm6.Core;
using Besm6.Loader;

namespace Besm6.Tests
{
    /// <summary>
    /// Порт CERNlib-фикстуры (ref/tests/fixture_machine.h:96-143,
    /// cernlib_test.cpp:100-129): собирает job-файл «пролог + ref/tests/libN/{name}.f
    /// + *end file», грузит на барабан #1, бутит MONSYS, компилирует FORTRAN,
    /// линкует CERN-библиотеку с ленты 012 (librar.12), исполняет и строго
    /// сравнивает stdout с ref/tests/libN/expect_{name}.txt.
    /// Артефакты случая (Task A2): tests-run/cernlib/lib{N}/{name}/{actual.txt,diff.txt,run.json}.
    /// Консольная редирекция всегда восстанавливается (finally в Run / Cleanup).
    /// </summary>
    public sealed class CernLibFixture
    {
        private readonly StringBuilder _output = new();
        private TextWriter? _savedOut;
        private MachineCore _machine = null!;
        private DubnaLoader _loader = null!;
        private string? _root; // каталог с ref/ и tests-run/
        private readonly string? _rootOverride;

        /// <summary>
        /// Создаёт фикстуру. <paramref name="rootOverride"/> — корневой каталог с
        /// ref/tests (изолированные тесты используют синтетический корень); null — автопоиск.
        /// </summary>
        public CernLibFixture(string? rootOverride = null) => _rootOverride = rootOverride;

        public string Output => _output.ToString();
        public long Instructions => _loader?.InstructionsExecuted ?? 0;

        /// <summary>Лимит инструкций (по умолчанию 1e9, как в DubnaLoader). Превышение → LimitExceeded.</summary>
        public long InstructionLimit { get; set; } = 1_000_000_000L;

        /// <summary>Лимит wall-clock времени на случай в мс (по умолчанию 120 c). Превышение → LimitExceeded.</summary>
        public long WallClockLimitMs { get; set; } = 300_000L;

        /// <summary>Тест-хук: вызывается между записью job-файла и RunScript (в продакшене null).</summary>
        internal Action? TestHookBeforeRunScript { get; set; }

        public void Setup()
        {
            _output.Clear();
            _savedOut = Console.Out;
            Console.SetOut(new StringWriter(_output));
            _machine = new MachineCore();
            _loader = new DubnaLoader(_machine) { Verbose = false };
            _loader.Output = s => _output.Append(s);
            // EOF: не ждать консольного ввода (E71 case 6 — защита от зависания).
            _loader.Input = _ => "";
            _root = _rootOverride ?? FindRoot();
        }

        /// <summary>Восстанавливает консоль (идемпотентно — безопасно вызывать многократно).</summary>
        public void Cleanup()
        {
            if (_savedOut != null)
            {
                Console.SetOut(_savedOut);
                _savedOut = null;
            }
        }

        /// <summary>Корневой каталог (ленивое разрешение: работает и до Setup).</summary>
        private string Root => _root ??= _rootOverride ?? FindRoot();

        public string RefTestsDir
        {
            get
            {
                string direct = Path.Combine(Root, "ref", "tests");
                return Directory.Exists(direct)
                    ? direct
                    : Path.Combine(Root, "ref", "dubna", "tests");
            }
        }
        public string ArtifactsDir => Path.Combine(Root, "tests-run", "cernlib");

        /// <summary>
        /// Каталог артефактов одного случая (Task A2): tests-run/cernlib/lib{lib}/{name}.
        /// lib1/x и lib2/x — разные каталоги: одинаковые имена в разных библиотеках
        /// не перезаписывают друг друга.
        /// </summary>
        public string ArtifactDir(int lib, string name) => Path.Combine(ArtifactsDir, "lib" + lib, name);

        /// <summary>
        /// Исполнить один CERNlib-случай и вернуть структурированный результат (Task A2).
        /// Классификации: Pass / OutputMismatch / LimitExceeded / LoaderError / MissingSource.
        /// При любом исходе консольная редирекция восстанавливается (finally).
        /// При любом неудачном исходе пишутся артефакты: actual.txt, diff.txt, run.json.
        /// </summary>
        public CernLibRunResult Run(CernLibCase c) => Run(c.Library, c.Name);

        public CernLibRunResult Run(int lib, string name)
        {
            string libDir = Path.Combine(RefTestsDir, "lib" + lib);
            string src = Path.Combine(libDir, name + ".f");
            string expectPath = Path.Combine(libDir, "expect_" + name + ".txt");
            if (!File.Exists(src) || !File.Exists(expectPath))
            {
                string message = "нет исходника/expect: " + libDir;
                string missingExpect = File.Exists(expectPath)
                    ? NormalizeLineEndings(File.ReadAllText(expectPath))
                    : string.Empty;
                Cleanup();
                return CompleteRun(lib, name, CernLibClassification.MissingSource,
                    LoadResult.Failed(message, 0, 0), 0, string.Empty, missingExpect,
                    message, instructionLimitExceeded: false, wallClockLimitExceeded: false);
            }

            _output.Clear(); // защита от накопления вывода при повторных Run без Setup
            var watch = System.Diagnostics.Stopwatch.StartNew();
            LoadResult result;
            try
            {
                _loader.InstructionLimit = InstructionLimit;
                // Wall-clock стоп ВНУТРИ цикла шагов: зациклившийся случай
                // завершается за ~WallClockLimitMs с классификацией LimitExceeded,
                // а не жжёт весь instruction-лимит (см. lib2/f004b, 31.08.2026).
                _loader.WallClockLimitMs = WallClockLimitMs;
                string jobPath = WriteJobFile(lib, name, src);
                TestHookBeforeRunScript?.Invoke();
                result = _loader.RunScript(jobPath);
            }
            catch (Exception ex)
            {
                watch.Stop();
                string message = ex.ToString();
                return CompleteRun(lib, name, CernLibClassification.LoaderError,
                    LoadResult.Failed(message, 0, _loader.InstructionsExecuted),
                    watch.ElapsedMilliseconds, NormalizeLineEndings(_output.ToString()),
                    NormalizeLineEndings(File.ReadAllText(expectPath)), message,
                    instructionLimitExceeded: false, wallClockLimitExceeded: false);
            }
            finally
            {
                Cleanup(); // редирекция консоли всегда восстанавливается (A2)
            }
            watch.Stop();

            string actual = NormalizeLineEndings(_output.ToString());
            string expect = NormalizeLineEndings(File.ReadAllText(expectPath));

            bool instrExceeded = result.LimitExceeded;
            bool wallExceeded = watch.ElapsedMilliseconds > WallClockLimitMs;
            CernLibClassification cls;
            if (instrExceeded || wallExceeded)
                cls = CernLibClassification.LimitExceeded;
            else if (!result.Success)
                cls = CernLibClassification.LoaderError;
            else if (actual == expect)
                cls = CernLibClassification.Pass;
            else
                cls = CernLibClassification.OutputMismatch;

            return CompleteRun(lib, name, cls, result, watch.ElapsedMilliseconds, actual, expect,
                result.Success ? null : result.ToString(), instrExceeded, wallExceeded);
        }

        private CernLibRunResult CompleteRun(int lib, string name, CernLibClassification cls,
            LoadResult result, long elapsedMs, string actual, string expect, string? loaderMessage,
            bool instructionLimitExceeded, bool wallClockLimitExceeded)
        {
            int? firstDiff = null;
            string? ctxExpect = null;
            string? ctxActual = null;
            if (actual != expect)
            {
                // Первая точка расхождения + контекст 60/160 символов.
                int p = 0;
                int lim = Math.Min(actual.Length, expect.Length);
                while (p < lim && actual[p] == expect[p]) p++;
                int from = Math.Max(0, p - 60);
                int show = 160;
                firstDiff = p;
                ctxExpect = expect.Substring(from, Math.Min(show, expect.Length - from));
                ctxActual = actual.Substring(from, Math.Min(show, actual.Length - from));
            }

            string? actualPath = null;
            string? diffPath = null;
            string? runInfoPath = null;
            if (cls != CernLibClassification.Pass)
            {
                string dir = ArtifactDir(lib, name);
                Directory.CreateDirectory(dir);
                actualPath = Path.Combine(dir, "actual.txt");
                File.WriteAllText(actualPath, actual, new UTF8Encoding(false));
                diffPath = Path.Combine(dir, "diff.txt");
                File.WriteAllText(diffPath,
                    UnifiedDiff("expect_" + name + ".txt", "actual_" + name + ".txt", expect, actual),
                    new UTF8Encoding(false));
                runInfoPath = Path.Combine(dir, "run.json");
                File.WriteAllText(runInfoPath, RunInfoJson(lib, name, cls, result,
                    elapsedMs, actual.Length, expect.Length, firstDiff, ctxExpect, ctxActual),
                    new UTF8Encoding(false));
            }

            return new CernLibRunResult
            {
                Library = lib,
                Name = name,
                Classification = cls,
                Instructions = result.Instructions,
                ElapsedMs = elapsedMs,
                InstructionLimitExceeded = instructionLimitExceeded,
                WallClockLimitExceeded = wallClockLimitExceeded,
                LoaderMessage = loaderMessage,
                ActualText = actual,
                ExpectText = expect,
                FirstDiffPosition = firstDiff,
                FirstDiffExpected = ctxExpect,
                FirstDiffActual = ctxActual,
                ActualPath = actualPath,
                DiffPath = diffPath,
                RunInfoPath = runInfoPath,
            };
        }

        /// <summary>Старая сигнатура (совместимость: W303 и пр.): делегирует в Run.</summary>
        public bool RunAndCompare(int lib, string name, out string actual, out string expect, out string diagnostics)
        {
            CernLibRunResult r = Run(lib, name);
            actual = r.ActualText ?? string.Empty;
            expect = r.ExpectText ?? string.Empty;
            diagnostics = r.Success
                ? "OK (instructions: " + r.Instructions + ")"
                : r.Classification + ": " + r.LoaderMessage;
            return r.Success;
        }

        /// <summary>run.json: параметры запуска, счётчик инструкций, stop-reason, точка дивергенции.</summary>
        private string RunInfoJson(int lib, string name, CernLibClassification cls, LoadResult result,
            long elapsedMs, int actualChars, int expectChars, int? firstDiff, string? ctxExpect, string? ctxActual)
        {
            var info = new Dictionary<string, object?>
            {
                ["case"] = "lib" + lib + "/" + name,
                ["classification"] = cls.ToString(),
                ["instructions"] = result.Instructions,
                ["elapsedMs"] = elapsedMs,
                ["instructionLimit"] = InstructionLimit,
                ["wallClockLimitMs"] = WallClockLimitMs,
                ["instructionLimitExceeded"] = result.LimitExceeded,
                ["wallClockLimitExceeded"] = elapsedMs > WallClockLimitMs,
                ["loader"] = result.ToString(),
                ["actualChars"] = actualChars,
                ["expectChars"] = expectChars,
                ["firstDiffPosition"] = firstDiff,
                ["firstDiffExpected"] = ctxExpect,
                ["firstDiffActual"] = ctxActual,
                ["dotnet"] = Environment.Version.ToString(),
                ["os"] = Environment.OSVersion.ToString(),
            };
            return JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>
        /// «{PC:5oct} {R|L}: {octal(RK)}». Срабатывает в НАЧАЛЕ инструкции (после fetch RK,
        /// (без мнемоники/«= result»/«Drum …») — см. tests-run/_difftrace.ps1.
        /// </summary>
        public LoadResult GenerateTrace(int lib, string name, string tracePath)
        {
            string libDir = Path.Combine(RefTestsDir, "lib" + lib);
            string src = Path.Combine(libDir, name + ".f");
            if (!File.Exists(src)) throw new FileNotFoundException("нет исходника: " + src);

            string jobPath = WriteJobFile(lib, name, src);

            var traceWriter = new StreamWriter(tracePath, false, new UTF8Encoding(false));
            _loader.CppInstructionTrace = (pc, rightFlag, rk, opcode) =>
            {
                if (IsExtracodeTraced(opcode))
                    traceWriter.WriteLine(OctPc(pc) + " " + (rightFlag ? "R" : "L") + ": " + OctalInstr(rk));
            };
            LoadResult result;
            try
            {
                result = _loader.RunScript(jobPath);
            }
            finally
            {
                _loader.CppInstructionTrace = null;
                traceWriter.Flush();
                traceWriter.Close();
            }
            return result;
        }


        private static bool IsExtracodeTraced(uint opcode)
        {
            if (opcode == 0x3D) return false;                  // 0o75: E75 не трассируется
            if (opcode >= 0x28 && opcode <= 0x3F) return true; // 0o50..0o77: Э50..Э77 (короткие)
            if (opcode == 0x80 || opcode == 0x88) return true; // 0o200, 0o210: Э20, Э21 (длинная форма)
            return false;
        }

        /// <summary>PC в 5 восьмеричных разрядах (std::setfill('0') << std::setw(5) в print_instruction).</summary>
        private static string OctPc(uint pc) => Convert.ToString(pc & 0x7FFF, 8).PadLeft(5, '0');

        /// <summary>Число в N восьмеричных разрядах (std::setfill('0') << std::setw(N)).</summary>
        private static string Oct(int x, int width) => Convert.ToString(x, 8).PadLeft(width, '0');

        /// <summary>
        /// besm6_print_instruction_octal (ref/besm6_arch.cpp:280):
        /// reg(2) + [длинная: mid(2) addr(5)] | [короткая: op(3) addr(4)].
        /// </summary>
        private static string OctalInstr(uint rk)
        {
            int reg = (int)(rk >> 20) & 0x0F;
            if ((rk & 0x80000u) != 0)   // ONEBIT(20) — длинная инструкция
            {
                int mid = (int)((rk >> 15) & 0x1F);   // 0o37
                int addrL = (int)(rk & 0x7FFF);       // 0o77777
                return Oct(reg, 2) + " " + Oct(mid, 2) + " " + Oct(addrL, 5);
            }
            int op = (int)((rk >> 12) & 0x7F);        // 0o177
            int addr = (int)(rk & 0xFFF);             // 0o7777
            return Oct(reg, 2) + " " + Oct(op, 3) + " " + Oct(addr, 4);
        }

        // ---------------------------------------------------------------
        // ---------------------------------------------------------------
        private string WriteJobFile(int lib, string name, string srcPath)

        {
            string prolog = "*name " + name + "\n" +
                            "*tape:12/librar,32\n" +
                            "*library:1,2,3,5,12,23\n" +
                            "*call setftn:one,long\n" +
                            "*no list\n" +
                            "*no load list\n";
            string epilog = "*end file\n";

            string jobsDir = Path.Combine(ArtifactsDir, "jobs");
            Directory.CreateDirectory(jobsDir);
            string jobPath = Path.Combine(jobsDir, "cernlib" + lib + "_" + name + ".dub");
            using (var fs = new FileStream(jobPath, FileMode.Create, FileAccess.Write))
            using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                sw.Write(prolog);
                sw.Write(File.ReadAllText(srcPath)); // как есть (LF) — COSY-кодирование
                sw.Write(epilog);
            }
            return jobPath;
        }

        // ---------------------------------------------------------------
        // Утилиты.
        // ---------------------------------------------------------------
        private static string FindRoot()
        {
            string? dir = Directory.GetCurrentDirectory();
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, "ref", "tests")) ||
                    Directory.Exists(Path.Combine(dir, "ref", "dubna", "tests")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }
            throw new DirectoryNotFoundException("ref/tests или ref/dubna/tests не найден (CWD: " +
                Directory.GetCurrentDirectory() + ")");
        }

        internal static string NormalizeLineEndings(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        /// <summary>Удобное отображение строк с управляющими символами для сообщений.</summary>
        internal static string Quote(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("\r", "␍").Replace("\n", "␊\n    ").Replace("\t", "␉");
        }

        /// <summary>Строковый diff (LCS по строкам, контекст 3 строки) для диагностики.</summary>
        internal static string UnifiedDiff(string aName, string bName, string a, string b)
        {
            string[] A = a.Replace("\r\n", "\n").Split('\n');
            string[] B = b.Replace("\r\n", "\n").Split('\n');
            int n = A.Length, m = B.Length;

            // Таблица LCS (обратный проход).
            int[,] dp = new int[n + 1, m + 1];
            for (int ri = n - 1; ri >= 0; ri--)
                for (int rj = m - 1; rj >= 0; rj--)
                    dp[ri, rj] = A[ri] == B[rj] ? dp[ri + 1, rj + 1] + 1 : Math.Max(dp[ri + 1, rj], dp[ri, rj + 1]);

            var ops = new List<(char Kind, string Text)>();
            int i = 0, j = 0;
            while (i < n && j < m)
            {
                if (A[i] == B[j]) { ops.Add((' ', A[i])); i++; j++; }
                else if (dp[i + 1, j] >= dp[i, j + 1]) { ops.Add(('-', A[i])); i++; }
                else { ops.Add(('+', B[j])); j++; }
            }
            while (i < n) { ops.Add(('-', A[i])); i++; }
            while (j < m) { ops.Add(('+', B[j])); j++; }

            int context = 3;
            var sb = new StringBuilder();
            sb.AppendLine("diff " + aName + " (L" + n + ")  vs  " + bName + " (L" + m + ")");
            for (int k = 0; k < ops.Count;)
            {
                if (ops[k].Kind == ' ')
                {
                    int first = k;
                    while (k < ops.Count && ops[k].Kind == ' ') k++;
                    int last = k - 1;
                    if (RunsNearChange(ops, first, last, context) || first < context || last >= ops.Count - 1 - context)
                        for (int t = first; t <= last; t++)
                            sb.AppendLine("  " + ops[t].Text);
                    else
                        sb.AppendLine("  ... (" + (last - first + 1) + " совпадающих строк, опущено)");
                }
                else
                {
                    sb.AppendLine(ops[k].Kind + " " + ops[k].Text);
                    k++;
                }
            }
            return sb.ToString();
        }

        private static bool RunsNearChange(List<(char Kind, string Text)> ops, int from, int to, int window)
        {
            for (int k = Math.Max(0, from - window); k <= Math.Min(ops.Count - 1, to + window); k++)
                if (ops[k].Kind != ' ')
                    return true;
            return false;
        }
    }

    /// <summary>
    /// Классификация результата исполнения случая (SuperPlan Task A2):
    /// превышение лимитов — отдельный класс, а не «общий output mismatch».
    /// </summary>
    public enum CernLibClassification
    {
        Pass,            // вывод совпал с expect
        OutputMismatch,  // вывод отличается от expect
        LimitExceeded,   // превышен instruction- или wall-clock лимит
        LoaderError,     // ошибка/исключение лоадера
        MissingSource,   // нет .f / expect_*.txt
    }

    /// <summary>Структурированный результат одного CERNlib-случая (SuperPlan Task A2).</summary>
    public sealed class CernLibRunResult
    {
        public int Library { get; init; }
        public string Name { get; init; } = string.Empty;
        public CernLibClassification Classification { get; init; }
        public long Instructions { get; init; }
        public long ElapsedMs { get; init; }
        public bool InstructionLimitExceeded { get; init; }
        public bool WallClockLimitExceeded { get; init; }
        public string? LoaderMessage { get; init; }
        public string? ActualText { get; init; }
        public string? ExpectText { get; init; }
        public int? FirstDiffPosition { get; init; }
        public string? FirstDiffExpected { get; init; }
        public string? FirstDiffActual { get; init; }
        public string? ActualPath { get; init; }
        public string? DiffPath { get; init; }
        public string? RunInfoPath { get; init; }

        public bool Success => Classification == CernLibClassification.Pass;
        public string Case => "lib" + Library + "/" + Name;

        public override string ToString() =>
            Case + " [" + Classification + "] instr=" + Instructions + " ms=" + ElapsedMs;
    }
}
