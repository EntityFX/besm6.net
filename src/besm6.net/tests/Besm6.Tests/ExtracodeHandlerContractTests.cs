using System;
using Besm6.Core;
using Besm6.Loader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    /// <summary>
    /// Контрактные тесты экстракодов без сценарной обвязки (audit P0-4, P1-8, P1-9).
    /// Референс: ref/extracode.cpp — case 051..056 (матфункции),
    /// 060/062/066/077 (default → «Unimplemented extracode»), 073 (no-op),
    /// 074 (throw ""), 200/210 (reserved/no-op).
    /// E51-E56 проверяют WIRING хендлера (правильная функция на ACC и обратно):
    /// численная точность Sin/Cos/... покрывается тестами Besm6Math.
    /// </summary>
    [TestClass]
    [TestCategory("Extracode")]
    public sealed class ExtracodeHandlerContractTests
    {
        // Значения opcode — десятичные (см. src/besm6.net/Core/Extracode.cs).
        private const int E20 = 128; // 200 oct
        private const int E21 = 136; // 210 oct
        private const int E50 = 40;  // 050 oct
        private const int E51 = 41;  // 051 oct
        private const int E52 = 42;  // 052 oct
        private const int E53 = 43;  // 053 oct
        private const int E54 = 44;  // 054 oct
        private const int E55 = 45;  // 055 oct
        private const int E56 = 46;  // 056 oct
        private const int E60 = 48;  // 060 oct
        private const int E62 = 50;  // 062 oct
        private const int E66 = 54;  // 066 oct
        private const int E72 = 58;  // 072 oct
        private const int E73 = 59;  // 073 oct
        private const int E74 = 60;  // 074 oct
        private const int E77 = 63;  // 077 oct

        private static ExtracodeHandler MakeHandler(MachineCore machine)
        {
            return new ExtracodeHandler(
                machine,
                diskByTapeId: id => null,
                diskByUnit: u => null,
                drumByUnit: u => null,
                output: s => { },
                input: p => "");
        }

        // ─── E74: «finish the job» — intentional halt (P0) ─────────────────
        /// <summary>
        /// ref/extracode.cpp L110-111: case 074 → throw Exception("") —
        /// ПУСТОЕ сообщение является сигналом завершения работы, а не ошибкой:
        /// DubnaLoader переводит его в Halt (Intercept с пустым сообщением).
        /// </summary>
        [TestMethod]
        public void E74_ThrowsEmptyMessage_ContractOfJobEnd()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);
            ulong accBefore = 0x200030004UL; // 48-bit word, safe 9-digit literal
            machine.Cpu.SetAcc(accBefore);

            try
            {
                handler.Handle(E74, 0);
                Assert.Fail("E74 обязан бросать ProcessorException");
            }
            catch (ProcessorException ex)
            {
                Assert.AreEqual(string.Empty, ex.Message,
                    "E74 = «finish the job»: пустое сообщение, как в C++ референсе");
            }

            // Состояние не трогается — остановка, а не обработка.
            Assert.AreEqual(accBefore, machine.Cpu.GetAcc().Value);
        }

        // ─── E73 / E20 / E21: no-op контракты (P1-9) ───────────────────────
        [TestMethod]
        [DataRow(E73)] // 073 — «Unknown, for ITM/ASS» (ref L107-108: break)
        [DataRow(E20)] // 200 — reserved/no-op
        [DataRow(E21)]
        public void NoopExtracodes_ReturnTrue_DoNotTouchState(int code)
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);
            ulong accBefore = 0x200030004UL;
            machine.Cpu.SetAcc(accBefore);
            machine.Cpu.SetM(15, 0x2001);

            bool handled = handler.Handle(code, 0);

            Assert.IsTrue(handled, $"э{Convert.ToString(code, 8)} обязан обработываться как no-op");
            Assert.AreEqual(accBefore, machine.Cpu.GetAcc().Value, "no-op не меняет ACC");
            Assert.AreEqual(0x2001u, machine.Cpu.GetM(15), "no-op не меняет M[15]");
        }

        [TestMethod]
        public void HangDetection_IsDisabledByDefaultLikeCpp()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            for (int i = 0; i < 502; i++)
                Assert.IsTrue(handler.Handle(E73, 0));
        }

        [TestMethod]
        public void E50_070200_ReturnsCppCapabilityMask()
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);
            machine.Cpu.SetM(14, 28800); // 070200 oct

            Assert.IsTrue(handler.Handle(E50, 0));
            Assert.AreEqual(0x8000UL, machine.Cpu.GetAcc().Value,
                "E50 070200 must return the capability mask used by the C++ reference");
        }

        // ─── E60 / E62 / E66 / E77: unsupported → false (P1-9) ──────────────
        /// <summary>
        /// ref/extracode.cpp default: throw "Unimplemented extracode ...".
        /// В C# контрактом является false от хендлера — CPU-исполнитель
        /// превращает его в ProcessorException("Extracode N not implemented").
        /// </summary>
        [TestMethod]
        [DataRow(E60)]
        [DataRow(E62)]
        [DataRow(E66)]
        [DataRow(E77)]
        public void UnsupportedExtracodes_ReturnFalse(int code)
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);

            bool handled = handler.Handle(code, 0);

            Assert.IsFalse(handled, $"э{Convert.ToString(code, 8)} не реализован и обязан вернуть false");
        }

        [TestMethod]
        [DataRow(4)]
        [DataRow(8)]
        [DataRow(0x7FFF)]
        public void E72_OnlyDocumentedRequestsAreNoop(int addr)
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);
            machine.Cpu.SetM(14, (uint)addr);

            Assert.IsTrue(handler.Handle(E72, 0));
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(1)]
        [DataRow(7)]
        public void E72_UnsupportedRequestsThrow(int addr)
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);
            machine.Cpu.SetM(14, (uint)addr);

            try
            {
                handler.Handle(E72, 0);
            }
            catch (ProcessorException ex)
            {
                Assert.AreEqual($"Unimplemented extracode *72 {Convert.ToString(addr, 8)}", ex.Message);
                return;
            }

            Assert.Fail("Недопустимый запрос E72 обязан бросать ProcessorException");
        }

        // ─── E51-E56: success-path wiring (P1-8) ────────────────────────────
        private static void CheckMathWiring(int code, uint addr, Func<ulong, ulong> fn, double input)
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);
            ulong word = Besm6Math.DoubleToBesm6(input);
            machine.Cpu.SetAcc(word);
            machine.Cpu.SetM(14, addr); // M[016] oct — адрес подкоманды

            Assert.IsTrue(handler.Handle(code, 0), "valid addr обязан обработаться");
            Assert.AreEqual(fn(word), machine.Cpu.GetAcc().Value,
                $"э{Convert.ToString(code, 8)} addr=0{Convert.ToString(addr, 8)} обязан применить fn к ACC");
        }

        [TestMethod]
        public void E51_Addr0_IsSin_Addr1_IsCos()
        {
            CheckMathWiring(E51, 0, Besm6Math.Sin, 0.5);
            CheckMathWiring(E51, 1, Besm6Math.Cos, 0.5);
        }

        [TestMethod]
        [DataRow(2)]
        [DataRow(3)]
        public void E51_InvalidAddr_Throws(int addr)
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);
            machine.Cpu.SetM(14, (uint)addr);

            bool threw = false;
            try { handler.Handle(E51, 0); } catch (ProcessorException) { threw = true; }
            Assert.IsTrue(threw, "э51 с недопустимым addr обязан бросать ProcessorException");
        }

        [TestMethod]
        public void E52_Addr0_IsCos() => CheckMathWiring(E52, 0, Besm6Math.Cos, 0.5);

        [TestMethod]
        public void E53_Addr0_IsAtan() => CheckMathWiring(E53, 0, Besm6Math.Atan, 1.0); // π/4

        [TestMethod]
        public void E54_Addr0_IsAsin() => CheckMathWiring(E54, 0, Besm6Math.Asin, 0.5); // π/6

        [TestMethod]
        public void E55_Addr0_IsLog() => CheckMathWiring(E55, 0, Besm6Math.Log, Math.E); // 1

        [TestMethod]
        public void E56_Addr0_IsExp() => CheckMathWiring(E56, 0, Besm6Math.Exp, 1.0); // e

        [TestMethod]
        [DataRow(E52)]
        [DataRow(E53)]
        [DataRow(E54)]
        [DataRow(E55)]
        [DataRow(E56)]
        public void E52ToE56_NonZeroAddr_Throw(int code)
        {
            var machine = new MachineCore();
            var handler = MakeHandler(machine);
            machine.Cpu.SetM(14, 2); // любой addr != 0

            bool threw = false;
            try { handler.Handle(code, 0); } catch (ProcessorException) { threw = true; }
            Assert.IsTrue(threw, $"э{Convert.ToString(code, 8)} addr!=0 обязан бросать «Unimplemented extracode»");
        }
    }
}

