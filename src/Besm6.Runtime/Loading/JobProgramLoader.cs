using System;
using System.Collections.Generic;
using System.Linq;
using Besm6.Assembler;
#pragma warning disable CS0618

namespace Besm6.Runtime
{
    /// <summary>
    /// Загрузка программ: raw words, assembler sections, запись script на drum.
    /// </summary>
    internal sealed class JobProgramLoader
    {
        private readonly MachineCore _machine;
        private readonly TapeMountService _tapes;
        private readonly Action<string>? _verboseLog;

        public const int DefaultLoadBase = 512;
        public int LoadedBase { get; private set; } = 0;

        public JobProgramLoader(MachineCore machine, TapeMountService tapes, Action<string>? verboseLog)
        {
            _machine = machine;
            _tapes = tapes;
            _verboseLog = verboseLog;
        }

        /// <summary>
        /// Записать job-скрипт на барабан #1 в формате COSY.
        /// </summary>
        public void WriteScriptToDrum(DubJob job, IEnumerable<string> rawLines)
        {
            var drum = _tapes.GetDrumByUnit(1)!;
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
                    if (trimmed.StartsWith("*forex", StringComparison.OrdinalIgnoreCase))
                        trimmed = "*fortran" + trimmed.Substring(6);
                    WriteCosyLine(drum, ref offset, trimmed);
                }
            }
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

        /// <summary>Загрузить raw-слова в память.</summary>
        public int LoadRawWords(DubJob job)
        {
            _tapes.MountRequestedTapes(job);
            int baseAddr = job.TransMain ?? DefaultLoadBase;
            for (int i = 0; i < job.RawWords.Count; i++)
            {
                int addr = (baseAddr + i) & 0x7FFF;
                _machine.Memory.Write((uint)addr, new Word48((ulong)job.RawWords[i]));
            }
            _machine.Cpu.SetK((uint)baseAddr);
            LoadedBase = baseAddr;
            if (_verboseLog != null)
                _verboseLog($"Loaded {job.RawWords.Count} raw words at 0{baseAddr:X}, start K=0{baseAddr:X}");
            return baseAddr;
        }

        /// <summary>Ассемблировать и загрузить программу.</summary>
        public int LoadAssembler(DubJob job)
        {
            _tapes.MountRequestedTapes(job);
            int baseAddr = job.TransMain ?? DefaultLoadBase;

            var textLines = new List<string>();
            var rawValues = new List<(int index, long value)>();
            int wordIdx = 0;
            foreach (var w in job.AssemProgram)
            {
                if (w.IsRaw)
                    rawValues.Add((wordIdx, w.Value));
                else if (!string.IsNullOrWhiteSpace(w.Text))
                    textLines.Add(w.Text!);
                wordIdx++;
            }

            var asmResult = new AutoDetectingAssembler().AssembleProgram(textLines, baseAddr);
            for (int i = 0; i < asmResult.Words.Count; i++)
            {
                int addr = (baseAddr + i) & 0x7FFF;
                _machine.Memory.Write((uint)addr, new Word48((ulong)asmResult.Words[i]));
            }

            foreach (var (idx, val) in rawValues)
            {
                int addr = (baseAddr + idx) & 0x7FFF;
                _machine.Memory.Write((uint)addr, new Word48((ulong)val));
            }

            _machine.Cpu.SetK((uint)baseAddr);
            LoadedBase = baseAddr;
            if (_verboseLog != null)
                _verboseLog($"Assembled {asmResult.Words.Count} words at 0{baseAddr:X}, start K=0{baseAddr:X}");
            return baseAddr;
        }
    }
}