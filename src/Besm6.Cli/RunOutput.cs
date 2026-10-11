using System.Security.Cryptography;
using System.Text.Json;
using Besm6.Core;

namespace Besm6.Cli;

/// <summary>Formats host diagnostics after execution; it neither changes the CPU nor rates guest benchmarks.</summary>
internal static class RunOutput
{
        internal static string OctW(ulong x, int width) => Convert.ToString((long)x, 8).PadLeft(width, '0');

        internal static string OctK(uint k) => Convert.ToString(k & 0x7FFF, 8).PadLeft(5, '0');

        /// <summary>besm6_print_instruction_octal: reg(2) + [длинная: mid(2) addr(5)] | [короткая: op(3) addr(4)].</summary>
        internal static string OctalInstr(uint rk)
        {
            int reg = (int)(rk >> 20) & 0x0F;
            if ((rk & 0x80000u) != 0)
            {
                int mid = (int)((rk >> 15) & 0x1F);
                int addrL = (int)(rk & 0x7FFF);
                return OctW((ulong)reg, 2) + " " + OctW((ulong)mid, 2) + " " + OctW((ulong)addrL, 5);
            }
            int op = (int)((rk >> 12) & 0x7F);
            int addr = (int)(rk & 0xFFF);
            return OctW((ulong)reg, 2) + " " + OctW((ulong)op, 3) + " " + OctW((ulong)addr, 4);
        }

        internal static void WriteStatistics(MachineCore machine, ExecutionStatistics statistics)
        {
            // Outside the measured loop: a reproducible fingerprint for benchmark parity checks.
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> wordBytes = stackalloc byte[6];
            for (uint address = 0; address < (uint)machine.Memory.Size; address++)
            {
                ulong word = machine.Memory.Read(address).Value;
                for (int i = 0; i < 6; i++) wordBytes[i] = (byte)(word >> (40 - 8 * i));
                hash.AppendData(wordBytes);
            }
            var finalState = new ProcessorSnapshot(machine.Cpu.State);
            var report = new
            {
                statistics,
                finalState = new { finalState.K, finalState.IsRightHalf, A = finalState.A.Value,
                    Y = finalState.Y.Value, finalState.R, finalState.C, finalState.ApplyC,
                    finalState.EffectiveAddress, finalState.InterceptCount, finalState.InterceptAddress,
                    finalState.M, ClockTicks = machine.Clock.Tick },
                memorySha256 = Convert.ToHexString(hash.GetHashAndReset()),
            };
            Console.WriteLine("Execution stats: " + JsonSerializer.Serialize(report,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }

        /// <summary>
        /// Дамп ОЗУ в SIMH-формате (6 байт на 48-битное слово) — вход для
        /// tools/besm6tape.py, которым дизассемблируется образ ОС «Дубна».
        /// </summary>
        internal static void DumpMemory(MachineCore machine, string path, int count)
        {
            int total = count > 0 ? count : 32768;   // 0o77777+1
            using var fs = File.Create(path);
            for (uint a = 0; a < (uint)total; a++)
            {
                ulong word = machine.Memory.Read(a).Value;
                for (int sh = 40; sh >= 0; sh -= 8)
                    fs.WriteByte((byte)((word >> sh) & 0xFF));
            }
            Console.Error.WriteLine($"memory dump: {total} words -> {path}");
        }

        /// <summary>besm6_print_word_octal: 4 группы по 4 восьмеричных разряда.</summary>
        internal static string Word48Oct(ulong v) =>
            OctW((v >> 36) & 0xFFF, 4) + " " + OctW((v >> 24) & 0xFFF, 4) + " " +
            OctW((v >> 12) & 0xFFF, 4) + " " + OctW(v & 0xFFF, 4);

        internal static string RegLine(string name, ulong val)
        {
            switch (name)
            {
                case "A": return "      A = " + Word48Oct(val);
                case "Y": return "      Y = " + Word48Oct(val);
                case "R": return "      R = " + OctW(val, 2);
                case "C": return "      C = " + OctW(val, 5);
                case "CLEARC": return "      Clear C";
                default: return "      " + name + " = " + OctW(val, 5);
            }
        }
}
