using System;
using Besm6.Assembler;

namespace Besm6.Cli
{
    /// <summary>
    /// Команда `asm` — ассемблирование строки в восьмеричное слово.
    /// </summary>
    public sealed class AsmCommand : ICommand
    {
        private static readonly AutoDetectingAssembler _asm = new();

        public string Name => "asm";
        public string Description => "Assemble an instruction source into an octal word";
        public string Usage => "besm6 asm <source> [source2 ...]";

        public int Execute(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine($"Usage: {Usage}");
                return 1;
            }

            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    ulong word = _asm.AssembleWord(args[i]);
                    Console.WriteLine($"0{Disassembler.ToOctal((long)word)}");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }
    }
}