using System;
using Besm6.Asm;

namespace Besm6.Cli
{
    /// <summary>
    /// Команда `disasm` — дизассемблирование восьмеричного слова.
    /// </summary>
    public sealed class DisasmCommand : ICommand
    {
        public string Name => "disasm";
        public string Description => "Disassemble an octal word into instruction";
        public string Usage => "besm6 disasm <octal_word> [octal_word2 ...]";

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
                    long word = Convert.ToInt64(args[i], 8);
                    Console.WriteLine(Disassembler.DisasmWord(word));
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