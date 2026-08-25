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
    /// Отличия от C++: при несоответствии НЕ перезаписывает expect-файл —
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

        // ---------------------------------------------------------------
        // job-файл: пролог + исходник + '*end file' (как в C++ fixture).
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

        /// <summary>\r\n и \r → \n (нормализация только при сравнении, как в C++).</summary>
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