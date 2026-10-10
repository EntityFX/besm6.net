using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class ArithmeticErrorControlTests
{
    private static ArithmeticErrorControl Control(HardwareTimeline timeline,
        ArithmeticErrorMode mode = ArithmeticErrorMode.Stop, bool stopOnInput = true) =>
        new(timeline, new(100), new(mode, stopOnInput));

    [TestMethod]
    [DataRow(100UL)]
    [DataRow(110UL)]
    public void InputFoldingAndPermissionHaveDifferentOrigins(ulong cycle)
    {
        var timeline = new HardwareTimeline(new(10));
        var control = new ArithmeticErrorControl(timeline, new(cycle), new(ArithmeticErrorMode.Stop));
        control.CheckInput(new(0), 11); // Even total control parity: independently bad.
        timeline.AdvanceTo(new(50));
        control.BeginOperation();
        ulong folded = 10 + cycle + cycle / 2;
        ulong permitted = 50 + 2 * cycle;
        timeline.AdvanceTo(new(folded - 1));
        Assert.IsNull(control.InputCheckedAt);
        timeline.AdvanceTo(new(folded));
        Assert.AreEqual(new HardwareInstant(folded), control.InputCheckedAt);
        Assert.AreEqual(ArithmeticErrorSignals.None, control.Signals);
        timeline.AdvanceTo(new(permitted - 1));
        Assert.IsFalse(control.InputControlPermission);
        timeline.AdvanceTo(new(permitted));
        Assert.IsTrue(control.InputControlPermission);
        Assert.AreEqual(ArithmeticErrorSignals.Interruption | ArithmeticErrorSignals.InputControl, control.Signals);
        Assert.IsTrue(control.BlocksNextOperation);
        Assert.IsNull(control.LastInterruptSample); // The IU receives the cause at IZOP.
        control.CompleteOperation(true);
        Assert.IsTrue(control.CompletionHeld);
        Assert.AreEqual(11, control.LastInterruptSample!.Value.InputSource);
        Assert.AreEqual(control.Signals, control.LastInterruptSample.Value.Signals);
        Assert.AreEqual(ArithmeticErrorSignals.None, control.Signals & ArithmeticErrorSignals.PositiveOverflow);
    }

    [TestMethod]
    public void LateInputErrorWaitsForFoldingAndValidInputCannotEraseLatchedInterruption()
    {
        var timeline = new HardwareTimeline();
        var control = Control(timeline);
        control.BeginOperation();
        timeline.AdvanceTo(new(200));
        control.CheckInput(new(0), 6);
        timeline.AdvanceTo(new(349));
        Assert.AreEqual(ArithmeticErrorSignals.None, control.Signals);
        timeline.AdvanceTo(new(350));
        Assert.IsTrue(control.BlocksNextOperation);
        control.CheckInput(new(1), 7); // Odd total parity: valid.
        timeline.AdvanceTo(new(500));
        Assert.IsTrue(control.BlocksNextOperation);
        control.CompleteOperation(false);
        Assert.IsTrue(control.CompletionHeld);
        // The source code of the failed transfer must survive later valid input.
        Assert.AreEqual(6, control.LastInterruptSample!.Value.InputSource);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void GoodOrInhibitedInputDoesNotCreateAnError(bool good)
    {
        var timeline = new HardwareTimeline();
        var control = Control(timeline);
        control.CheckInput(new(good ? 1UL : 0UL), 3, enabled: good);
        control.BeginOperation();
        timeline.AdvanceTo(new(300));
        Assert.AreEqual(ArithmeticErrorSignals.None, control.Signals);
        control.CompleteOperation(false);
        Assert.IsFalse(control.CompletionHeld);
        Assert.IsNull(control.LastInterruptSample);
        Assert.IsNull(timeline.NextEventTime); // No release event for absent error signals.
    }

    [TestMethod]
    [DataRow((int)ArithmeticErrorMode.Stop, true)]
    [DataRow((int)ArithmeticErrorMode.ContinueAndReport, false)]
    [DataRow((int)ArithmeticErrorMode.ContinueArithmeticErrors, false)]
    [DataRow((int)ArithmeticErrorMode.InputControlOnly, false)]
    public void OverflowPoliciesKeepCauseAndCompletionDistinct(int modeCode, bool held)
    {
        var mode = (ArithmeticErrorMode)modeCode;
        var timeline = new HardwareTimeline();
        var control = Control(timeline, mode);
        control.BeginOperation();
        timeline.AdvanceTo(new(300));
        control.CompleteOperation(true);
        Assert.AreEqual(held, control.CompletionHeld);
        Assert.IsTrue((control.Signals & ArithmeticErrorSignals.PositiveOverflow) != 0);
        bool reports = mode != ArithmeticErrorMode.InputControlOnly;
        Assert.AreEqual(reports, control.BlocksNextOperation);
        Assert.AreEqual(reports, control.LastInterruptSample.HasValue);
        if (held)
        {
            timeline.AdvanceTo(new(1000));
            Assert.IsTrue(control.BlocksNextOperation);
            control.ReleaseSingleOperation();
        }
        else
        {
            timeline.AdvanceTo(new(399));
            Assert.IsTrue((control.Signals & ArithmeticErrorSignals.PositiveOverflow) != 0);
            timeline.AdvanceTo(new(400));
        }
        Assert.AreEqual(ArithmeticErrorSignals.None, control.Signals);
        Assert.IsFalse(control.BlocksNextOperation);
    }

    [TestMethod]
    [DataRow((int)ArithmeticErrorMode.Stop, true)]
    [DataRow((int)ArithmeticErrorMode.ContinueAndReport, false)]
    [DataRow((int)ArithmeticErrorMode.ContinueArithmeticErrors, false)]
    [DataRow((int)ArithmeticErrorMode.InputControlOnly, false)]
    public void InvalidDivisorSignalEndsAtIzopOnlyInContinuingPolicies(int modeCode, bool held)
    {
        var mode = (ArithmeticErrorMode)modeCode;
        var timeline = new HardwareTimeline();
        var control = Control(timeline, mode);
        control.BeginOperation();
        timeline.AdvanceTo(new(300));
        control.IndicateInvalidDivisor();
        var arithmetic = ArithmeticErrorSignals.PositiveOverflow | ArithmeticErrorSignals.InvalidDivisor;
        Assert.AreEqual(arithmetic, control.Signals & arithmetic);
        timeline.AdvanceTo(new(400));
        control.CompleteOperation(false);
        Assert.AreEqual(held, control.CompletionHeld);
        Assert.AreEqual(held, (control.Signals & ArithmeticErrorSignals.InvalidDivisor) != 0);
        if (mode != ArithmeticErrorMode.InputControlOnly)
            Assert.AreEqual(arithmetic, control.LastInterruptSample!.Value.Signals & arithmetic);
        else Assert.IsNull(control.LastInterruptSample);
        timeline.AdvanceTo(new(500));
        Assert.AreEqual(held, control.BlocksNextOperation);
    }

    [TestMethod]
    [DataRow((int)ArithmeticErrorMode.ContinueArithmeticErrors, true)]
    [DataRow((int)ArithmeticErrorMode.ContinueArithmeticErrors, false)]
    [DataRow((int)ArithmeticErrorMode.InputControlOnly, true)]
    [DataRow((int)ArithmeticErrorMode.InputControlOnly, false)]
    public void InputStopSettingIsIndependentOfArithmeticErrorSuppression(int modeCode, bool stop)
    {
        var mode = (ArithmeticErrorMode)modeCode;
        var timeline = new HardwareTimeline();
        var control = Control(timeline, mode, stop);
        control.CheckInput(new(0), 9);
        control.BeginOperation();
        timeline.AdvanceTo(new(300));
        control.CompleteOperation(true);
        Assert.AreEqual(stop, control.CompletionHeld);
        Assert.AreEqual(ArithmeticErrorSignals.InputControl | ArithmeticErrorSignals.Interruption,
            control.LastInterruptSample!.Value.Signals);
        timeline.AdvanceTo(new(400));
        Assert.AreEqual(stop, control.BlocksNextOperation);
    }

    [TestMethod]
    public void SingleOperationReleasePreservesExternalCauseAndChecksUnchangedBadInputAgain()
    {
        var timeline = new HardwareTimeline();
        var control = Control(timeline);
        Assert.ThrowsExactly<InvalidOperationException>(() => control.ReleaseSingleOperation());
        control.CheckInput(new(0), 4);
        control.BeginOperation();
        Assert.ThrowsExactly<InvalidOperationException>(() => control.ReleaseSingleOperation());
        timeline.AdvanceTo(new(300));
        control.CompleteOperation(false);
        var captured = control.LastInterruptSample;
        control.ReleaseSingleOperation();
        Assert.IsFalse(control.BlocksNextOperation);
        Assert.AreEqual(captured, control.LastInterruptSample);
        control.BeginOperation();
        timeline.AdvanceTo(new(500));
        Assert.IsTrue(control.BlocksNextOperation);
    }

    [TestMethod]
    public void PreviousReleaseCannotEraseNewerSignals()
    {
        var timeline = new HardwareTimeline();
        var control = Control(timeline, ArithmeticErrorMode.InputControlOnly);
        control.BeginOperation();
        timeline.AdvanceTo(new(300));
        control.CompleteOperation(true);
        control.BeginOperation();
        // Explicit supplied indication; this vector tests signal ownership,
        // not a complete division microsequence or a whole instruction cost.
        timeline.AdvanceTo(new(350));
        control.IndicateInvalidDivisor();
        timeline.AdvanceTo(new(400));
        Assert.AreEqual(ArithmeticErrorSignals.PositiveOverflow | ArithmeticErrorSignals.InvalidDivisor, control.Signals);
    }

    [TestMethod]
    public void GeneralClearCancelsOnlyOwnedEventsAndPreservesExternalSample()
    {
        var timeline = new HardwareTimeline();
        var control = Control(timeline, ArithmeticErrorMode.ContinueAndReport);
        control.BeginOperation();
        timeline.AdvanceTo(new(300));
        control.CompleteOperation(true);
        var captured = control.LastInterruptSample;
        control.CheckInput(new(0), 5);
        bool unrelated = false;
        timeline.ScheduleAt(new(600), () => unrelated = true);
        control.ApplyGeneralClearSignal();
        Assert.AreEqual(captured, control.LastInterruptSample);
        Assert.AreEqual(new HardwareInstant(600), timeline.NextEventTime);
        timeline.AdvanceTo(new(600));
        Assert.IsTrue(unrelated);
        Assert.IsNull(control.InputCheckedAt);
        Assert.AreEqual(ArithmeticErrorSignals.None, control.Signals);
        control.BeginOperation();
        control.ApplyGeneralClearSignal();
        timeline.AdvanceTo(new(1000));
        Assert.IsFalse(control.InputControlPermission);
        Assert.IsFalse(control.OperationActive);
    }

    [TestMethod]
    public void SchedulingOverflowDoesNotPartiallyStartOrCompleteOperation()
    {
        var timeline = new HardwareTimeline(new(ulong.MaxValue - 100));
        var control = Control(timeline);
        Assert.ThrowsExactly<OverflowException>(() => control.BeginOperation());
        Assert.ThrowsExactly<OverflowException>(() => control.CheckInput(new(0), 1));
        Assert.IsFalse(control.OperationActive);
        Assert.IsNull(timeline.NextEventTime);
        var lateTimeline = new HardwareTimeline(new(ulong.MaxValue - 300));
        var late = Control(lateTimeline, ArithmeticErrorMode.ContinueAndReport);
        late.BeginOperation();
        lateTimeline.AdvanceTo(new(ulong.MaxValue - 50));
        Assert.ThrowsExactly<OverflowException>(() => late.CompleteOperation(true));
        Assert.IsTrue(late.OperationActive);
        Assert.AreEqual(ArithmeticErrorSignals.None, late.Signals);
        Assert.IsNull(late.LastInterruptSample);
    }

    [TestMethod]
    public void InvalidConfigurationAndMissingOperationAreRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new ArithmeticErrorControl(null!, new(100), new()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ArithmeticErrorControl(new(), new(101), new()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ArithmeticErrorControl(new(), new(100), new((ArithmeticErrorMode)99)));
        var timeline = new HardwareTimeline();
        var control = Control(timeline);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => control.CheckInput(new(0), 16));
        Assert.IsNull(timeline.NextEventTime);
        Assert.ThrowsExactly<InvalidOperationException>(() => control.IndicateInvalidDivisor());
        Assert.ThrowsExactly<InvalidOperationException>(() => control.CompleteOperation(false));
        control.BeginOperation();
        Assert.ThrowsExactly<InvalidOperationException>(() => control.BeginOperation());
    }
}
