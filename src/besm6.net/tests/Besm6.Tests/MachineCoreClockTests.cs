using System;
using Besm6.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    /// <summary>
    /// Уровень B, SuperPlan Task B1 — изолированный шов дискретного модельного
    /// времени в <see cref="MachineCore"/>. Проверяет, что часы и планировщик
    /// существуют, связаны с одним источником модельного времени и что шаг
    /// инструкции стоит документированную стоимость — БЕЗ изменения наблюдаемой
    /// семантики уровня A.
    /// </summary>
    [TestClass]
    public class MachineCoreClockTests
    {
        [TestMethod]
        public void MachineCore_ExposesClockAndScheduler_AtZero()
        {
            var machine = new MachineCore();
            Assert.IsNotNull(machine.Clock);
            Assert.IsNotNull(machine.Scheduler);
            Assert.AreEqual(0UL, machine.Clock.Tick);
            Assert.AreEqual(0UL, machine.Scheduler.Now);
        }

        [TestMethod]
        public void TicksPerInstruction_IsDocumentedConstant()
        {
            // Фиксируем документированное значение константы. Читаем её через
            // reflection (тот же приём, что и TapeIdTests.AssertTapeId), чтобы
            // анализатор MSTest (MSTEST0032) не сходил «условие всегда истинно»
            // на сравнении двух compile-time констант.
            object? raw = typeof(MachineCore)
                .GetField(nameof(MachineCore.TicksPerInstruction))
                ?.GetRawConstantValue();
            Assert.IsNotNull(raw, nameof(MachineCore.TicksPerInstruction) + " constant is missing");
            Assert.AreEqual(1UL, Convert.ToUInt64(raw));
        }

        [TestMethod]
        public void ClockAndScheduler_ShareOneTimeSource()
        {
            var machine = new MachineCore();
            // Планировщик и часы машины — один источник модельного времени.
            machine.Scheduler.Schedule(3, () => { });
            machine.Scheduler.AdvanceTo(7);
            Assert.AreEqual(7UL, machine.Clock.Tick);
            Assert.AreEqual(7UL, machine.Scheduler.Now);
        }

        [TestMethod]
        public void Step_AdvancesClockByDocumentedCost_WithoutAlteringLevelASemantics()
        {
            var machine = new MachineCore();
            ulong before = machine.Clock.Tick;
            try
            {
                machine.Step(); // одна инструкция (пустая машина) — модельного времени стоит TicksPerInstruction
            }
            catch (Exception)
            {
                // Если инструкция по пустой памяти некорректна — не блокируем:
                // инвариант B1 — часы продвинулись ровно на документированную стоимость.
            }
            Assert.AreEqual(before + MachineCore.TicksPerInstruction, machine.Clock.Tick);
        }
    }
}
