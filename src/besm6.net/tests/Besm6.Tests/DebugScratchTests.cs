using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Besm6.Core;

namespace Besm6.Tests
{
    [TestClass]
    public sealed class DebugScratchTests
    {
        private sealed class LinearMemory : IMemory
        {
            private readonly Word48[] _words = new Word48[32768];
            public Word48 Read(uint address) => _words[address & 0x7FFF];
            public void Write(uint address, Word48 word) => _words[address & 0x7FFF] = word;
            public int Size => _words.Length;
        }

        [TestMethod]
        public void Debug_Literals()
        {
            ulong[] v =
            {
                0x1UL, 0xFUL, 0x10UL, 0xFFUL, 0x100UL, 0xFFFUL,
                0x1000UL, 0x10000UL, 0x100000UL, 0x1000000UL,
                0x10000000UL, 0x100000000UL, 0x1000000000UL, 0x10000000000UL,
                0x100000000000UL, 0x1000000000000UL,
                0x123456789ABCDEUL, 0x99999999999999UL, 0xABCDEF012345UL,
                0x200030004UL, 0x0ABCDEF01234UL, 0x11111111111111UL, 0x44444444444444UL,
            };
            var sb = new System.Text.StringBuilder();
            foreach (ulong x in v) sb.Append(x).Append(' ');
            throw new Exception(sb.ToString());
        }
    }
}
