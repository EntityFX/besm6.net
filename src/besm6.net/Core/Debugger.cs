using System;
using System.Collections.Generic;
using System.Linq;
namespace Besm6.Core
{
    /// <summary>
    /// Консольный отладчик для симулятора БЭСМ-6.
    /// Работает поверх <see cref="MachineCore"/> с движком <see cref="Processor"/>.
    /// </summary>
    public class Debugger
    {
        private readonly MachineCore _machine;
        private bool _isRunning;

        public Debugger(MachineCore machine)
        {
            _machine = machine;
            _isRunning = true;
        }

        public void Start()
        {
            Console.WriteLine("=== BESM-6 Console Debugger ===");
            Console.WriteLine("Commands: step, run, regs, dump <addr>, load <file>, dub <file.dub>, k <addr>, disasm <addr>, exit");
            Console.WriteLine("-------------------------------------------------------");

            while (_isRunning)
            {
                Console.Write($"[K:{_machine.Cpu.K:X5}] > ");
                string? input = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(input)) continue;

                string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string command = parts[0];

                switch (command)
                {
                    case "step":
                        Step();
                        break;
                    case "run":
                        Run();
                        break;
                    case "regs":
                        PrintRegisters();
                        break;
                    case "dump":
                        if (parts.Length > 1 && TryParseAddr(parts[1], out int dumpAddr))
                            DumpMemory(dumpAddr, 16);
                        else
                            Console.WriteLine("Usage: dump <address>");
                        break;
                    case "load":
                        if (parts.Length > 1)
                            LoadProgram(parts[1]);
                        else
                            Console.WriteLine("Usage: load <filename>");
                        break;
                    case "dub":
                        if (parts.Length > 1)
                            RunDubScript(parts[1]);
                        else
                            Console.WriteLine("Usage: dub <file.dub>");
                        break;
                    case "k":
                    case "pc": // совместимый псевдоним старой команды
                        if (parts.Length > 1 && TryParseAddr(parts[1], out int k))
                        {
                            _machine.Cpu.SetK((uint)k);
                            Console.WriteLine($"K = {k:X5}");
                        }
                        else
                        {
                            Console.WriteLine("Usage: k <address>");
                        }
                        break;
                    case "disasm":
                        if (parts.Length > 1 && TryParseAddr(parts[1], out int dAddr))
                            Disassemble(dAddr, 16);
                        else
                            Console.WriteLine("Usage: disasm <address>");
                        break;
                    case "exit":
                    case "quit":
                        _isRunning = false;
                        break;
                    default:
                        Console.WriteLine("Unknown command. Available: step, run, regs, dump, load, k, disasm, exit");
                        break;
                }
            }
        }

        private void Step()
        {
            try
            {
                bool stopped = _machine.Step();
                PrintRegisters();
                if (stopped)
                    Console.WriteLine("Machine halted (STOP).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private void Run()
        {
            Console.WriteLine("Running... (Press Ctrl+C or wait for STOP/Error)");
            try
            {
                _machine.Run();
                Console.WriteLine("Machine halted (STOP).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Execution aborted: {ex.Message}");
            }
        }

        private void PrintRegisters()
        {
            var cpu = _machine.Cpu;
            Console.WriteLine(
                $"K={cpu.K:X5}  A=0x{cpu.A:X12}  Y=0x{cpu.Y:X12}  R={cpu.R:X2}  C={cpu.C:X5}");
        }

        private void DumpMemory(int address, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int addr = (address + i) & 0x7FFF;
                Word48 w = _machine.Memory.Read((uint)addr);
                Console.WriteLine($"{addr:X5}: {w.ToOctal()}  ({w.Value:X12})");
            }
        }

        private void Disassemble(int address, int count)
        {
            // Упрощённый дизассемблер: показывает слово целиком (левая/правая половины).
            for (int i = 0; i < count; i++)
            {
                int addr = (address + i) & 0x7FFF;
                Word48 w = _machine.Memory.Read((uint)addr);
                ulong left = (w.Value >> 24) & 0xFFFFFFu;
                ulong right = w.Value & 0xFFFFFFu;
                Console.WriteLine($"{addr:X5}: L=0x{left:X6} R=0x{right:X6}");
            }
        }

        private void LoadProgram(string filename)
        {
            try
            {
                _machine.LoadBinary(filename);
                Console.WriteLine($"Loaded {filename} at K=0");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Load error: {ex.Message}");
            }
        }

        /// <summary>
        /// Загрузка и выполнение .dub job-скрипта через Besm6.Loader.
        /// </summary>
        private void RunDubScript(string filename)
        {
            try
            {
                var loader = new DubnaLoader(_machine) { Verbose = true };
                var result = loader.RunScript(filename);
                Console.WriteLine(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Dubna load error: {ex.Message}");
            }
        }

        private static bool TryParseAddr(string s, out int value)
        {
            // Поддерживаем восьмеричные (с ведущим '0'/'o') и десятичные адреса.
            s = s.Trim();
            if (s.StartsWith("o") || s.StartsWith("0"))
            {
                string oct = s.StartsWith("o") ? s.Substring(1) : s.TrimStart('0');
                if (oct.Length == 0) oct = "0";
                try
                {
                    value = Convert.ToInt32(oct, 8);
                    return true;
                }
                catch
                {
                    // fallthrough
                }
            }
            return int.TryParse(s, out value);
        }
    }
}
