using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    /// артефакты actual_/diff_ пишутся в tests-run/cernlib/.
    /// </summary>
    public sealed class CernLibFixture
    {
        private readonly StringBuilder _output = new();
        private TextWriter _savedOut;
        private MachineCore _machine;
        private DubnaLoader _loader;
        private string _root; // каталог с ref/ и tests-run/

        public string Output => _output.ToString();
        public long Instructions => _loader?.InstructionsExecuted ?? 0;

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
            _root = FindRoot();
        }

        public void Cleanup()
        {
            if (_savedOut != null)
                Console.SetOut(_savedOut);
        }

        public string RefTestsDir => Path.Combine(_root, "ref", "tests");
        public string ArtifactsDir => Path.Combine(_root, "tests-run", "cernlib");

        /// <summary>
        /// Исполнить один CERNlib-тест. Возвращает true, если вывод совпал с expect.
        /// </summary>
        public bool RunAndCompare(int lib, string name, out string actual, out string expect, out string diagnostics)
        {
            diagnostics = null;
            actual = null;
            expect = null;

            string libDir = Path.Combine(RefTestsDir, "lib" + lib);
            string src = Path.Combine(libDir, name + ".f");
            if (!File.Exists(src))
            {
                diagnostics = "нет исходника: " + src;
                return false;
            }
            string expectPath = Path.Combine(libDir, "expect_" + name + ".txt");
            if (!File.Exists(expectPath))
            {
                diagnostics = "нет expect-файла: " + expectPath;
                return false;
            }

            string jobPath = WriteJobFile(lib, name, src);
            LoadResult result = _loader.RunScript(jobPath);
            actual = NormalizeLineEndings(_output.ToString());
            expect = NormalizeLineEndings(File.ReadAllText(expectPath));

            if (actual == expect)
            {
                diagnostics = "OK (instructions: " + result.Instructions + ")";
                return true;
            }

            // Артефакты: полный actual и diff для диагностики.
            Directory.CreateDirectory(ArtifactsDir);
            File.WriteAllText(Path.Combine(ArtifactsDir, "actual_" + name + ".txt"), actual);
            string diff = UnifiedDiff("expect_" + name + ".txt", "actual_" + name + ".txt", expect, actual);
            File.WriteAllText(Path.Combine(ArtifactsDir, "diff_" + name + ".txt"), diff);
            diagnostics = "result: " + result + "; instructions: " + result.Instructions;
            return false;
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
            string dir = Directory.GetCurrentDirectory();
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir, "ref", "tests")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }
            throw new DirectoryNotFoundException("ref/tests не найден (CWD: " +
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
}