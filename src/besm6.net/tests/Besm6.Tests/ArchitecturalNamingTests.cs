using System;
using System.Collections.Generic;
using System.Linq;
using Besm6.Asm;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    [TestClass]
    public class ArchitecturalNamingTests
    {
        [TestMethod]
        public void ProcessorExposesDocumentedRegisterNames()
        {
            string[] expected = { "A", "Y", "R", "M", "C", "K" };
            string[] legacy = { "Acc", "Rmr", "Rau", "Mod", "PC" };
            string[] legacyAccessors =
            {
                "GetAcc", "SetAcc", "GetRmr", "SetRmr",
                "GetRau", "SetRau", "GetPc", "SetPc",
            };
            string[] actual = typeof(Processor)
                .GetProperties()
                .Select(property => property.Name)
                .ToArray();
            string[] actualMethods = typeof(Processor)
                .GetMethods()
                .Select(method => method.Name)
                .ToArray();

            CollectionAssert.IsSubsetOf(expected, actual);
            foreach (string name in legacy)
                Assert.IsFalse(actual.Contains(name), $"Legacy register name {name} must not remain public.");
            foreach (string name in legacyAccessors)
                Assert.IsFalse(actualMethods.Contains(name), $"Legacy register accessor {name} must not remain public.");
        }

        [TestMethod]
        public void OpcodeEnumUsesDocumentedInstructionNames()
        {
            var expected = new (ushort Value, string Name)[]
            {
                (0x00, "Atx"), (0x01, "Stx"), (0x02, "Mod"), (0x03, "Xts"),
                (0x04, "APlusX"), (0x05, "AMinusX"), (0x06, "XMinusA"), (0x07, "Amx"),
                (0x08, "Xta"), (0x09, "Aax"), (0x0A, "Aex"), (0x0B, "Arx"),
                (0x0C, "Avx"), (0x0D, "Aox"), (0x0E, "ADivX"), (0x0F, "AMulX"),
                (0x10, "Apx"), (0x11, "Aux"), (0x12, "Acx"), (0x13, "Anx"),
                (0x14, "EPlusX"), (0x15, "EMinusX"), (0x16, "Asx"), (0x17, "Xtr"),
                (0x18, "Rte"), (0x19, "Yta"), (0x1A, "Ext"), (0x1B, "Op33"),
                (0x1C, "EPlusN"), (0x1D, "EMinusN"), (0x1E, "Asn"), (0x1F, "Ntr"),
                (0x20, "Ati"), (0x21, "Sti"), (0x22, "Ita"), (0x23, "Its"),
                (0x24, "Mtj"), (0x25, "JPlusM"), (0x26, "Op46"), (0x27, "Op47"),
                (0x90, "Utc"), (0x98, "Wtc"), (0xA0, "Vtm"), (0xA8, "Utm"),
                (0xB0, "Uza"), (0xB8, "U1a"), (0xC0, "Uj"), (0xC8, "Vjm"),
                (0xD0, "Ij"), (0xD8, "Stop"), (0xE0, "Vzm"), (0xE8, "V1m"),
                (0xF0, "Op36"), (0xF8, "Vlm"),
            };

            foreach ((ushort value, string name) in expected)
                Assert.AreEqual(name, Enum.GetName(typeof(Opcode), value), $"Opcode 0x{value:X2}");
        }

        [TestMethod]
        public void RegisterTraceUsesDocumentedRegisterNames()
        {
            var machine = new MachineCore();
            ulong utcOne = (((ulong)Opcode.Utc << 12) | 1UL) << 24;
            machine.Memory.Write(1, new Word48(utcOne));
            machine.Cpu.SetK(1);

            var names = new List<string>();
            machine.RegisterTrace = (name, _) => names.Add(name);
            machine.BeginRegisterTrace();
            machine.Cpu.SetA(1);
            machine.Cpu.SetY(2);
            machine.Cpu.SetR((ulong)RFlags.Log);

            machine.Step();

            CollectionAssert.AreEqual(new[] { "A", "Y", "R", "C" }, names);
        }

        [TestMethod]
        public void ExtMnemonicUsesDocumentedOpcode032()
        {
            Assert.AreEqual("ext", OpcodeTable.GetOpName((int)Opcode.Ext));
            Assert.AreEqual("*33", OpcodeTable.GetOpName((int)Opcode.Op33));
        }

        [TestMethod]
        public void LegacyNumericMnemonicForOpcode032RemainsAccepted()
        {
            Assert.IsTrue(OpcodeTable.TryGetOpcode("*32", out int opcode));
            Assert.AreEqual((int)Opcode.Ext, opcode);
        }

        [TestMethod]
        public void LoadResultExposesProgramCounterAsK()
        {
            Assert.IsNotNull(typeof(LoadResult).GetProperty("K"));
            Assert.IsNull(typeof(LoadResult).GetProperty("Pc"));
        }
    }
}
