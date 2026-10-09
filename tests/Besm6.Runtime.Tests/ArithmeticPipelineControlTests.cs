using Besm6.Runtime.Timing;

namespace Besm6.Tests;

[TestClass]
[TestCategory("HardwareTiming")]
public sealed class ArithmeticPipelineControlTests
{
    [TestMethod]
    public void OperandNeedsBothPermissionAndReadiness()
    {
        var pipeline = new ArithmeticPipelineControl();
        pipeline.Commands.TryReceive(7);
        Assert.IsFalse(pipeline.TryAcceptOperand(true));
        Assert.AreEqual(1, pipeline.Commands.Count);
        pipeline.GrantCommandPermission();
        Assert.IsFalse(pipeline.TryAcceptOperand(false));
        Assert.IsTrue(pipeline.CommandPermission);
        Assert.IsFalse(pipeline.TryStartOperation());
        Assert.IsTrue(pipeline.TryAcceptOperand(true));
        Assert.AreEqual(0, pipeline.Commands.Count);
        Assert.AreEqual(7U, pipeline.PreparedCommand);
        Assert.IsTrue(pipeline.TryStartOperation());
        Assert.AreEqual(7U, pipeline.ActiveCommand);
    }

    [TestMethod]
    public void SuccessorCanPrepareBeforePreviousCompletionButCannotStart()
    {
        var pipeline = new ArithmeticPipelineControl();
        pipeline.Commands.TryReceive(11);
        pipeline.Commands.TryReceive(22);
        pipeline.GrantCommandPermission();
        Assert.IsTrue(pipeline.TryAcceptOperand(true));
        Assert.IsTrue(pipeline.TryStartOperation());
        pipeline.GrantCommandPermission(); // RPK before predecessor IZOP is permitted.
        Assert.IsTrue(pipeline.TryAcceptOperand(true));
        Assert.AreEqual(22U, pipeline.PreparedCommand);
        Assert.AreEqual(11U, pipeline.ActiveCommand);
        Assert.IsFalse(pipeline.TryStartOperation());
        Assert.AreEqual(11U, pipeline.CompleteOperation());
        Assert.IsTrue(pipeline.TryStartOperation());
        Assert.AreEqual(22U, pipeline.CompleteOperation());
    }

    [TestMethod]
    public void CompletionDoesNotWaitForSuccessorOrInventPermission()
    {
        var pipeline = new ArithmeticPipelineControl();
        pipeline.Commands.TryReceive(0);
        pipeline.GrantCommandPermission();
        pipeline.TryAcceptOperand(true);
        pipeline.TryStartOperation();
        Assert.AreEqual(0U, pipeline.CompleteOperation());
        Assert.IsNull(pipeline.ActiveCommand);
        Assert.IsNull(pipeline.PreparedCommand);
        Assert.IsFalse(pipeline.CommandPermission);
        Assert.IsFalse(pipeline.TryStartOperation());
        Assert.ThrowsExactly<InvalidOperationException>(() => pipeline.CompleteOperation());
    }

    [TestMethod]
    public void APreparedOperandCannotBeOverwrittenByAnotherCommand()
    {
        var pipeline = new ArithmeticPipelineControl();
        pipeline.Commands.TryReceive(1);
        pipeline.Commands.TryReceive(2);
        pipeline.GrantCommandPermission();
        Assert.IsTrue(pipeline.TryAcceptOperand(true));
        pipeline.GrantCommandPermission();
        Assert.IsFalse(pipeline.TryAcceptOperand(true));
        Assert.AreEqual(1U, pipeline.PreparedCommand);
        Assert.AreEqual(1, pipeline.Commands.Count);
        Assert.IsTrue(pipeline.CommandPermission);
        pipeline.TryStartOperation();
        Assert.IsTrue(pipeline.TryAcceptOperand(true));
        Assert.AreEqual(2U, pipeline.PreparedCommand);
    }

    [TestMethod]
    public void EarlyPermissionWithoutCommandDoesNotLoseItsIndication()
    {
        var pipeline = new ArithmeticPipelineControl();
        pipeline.GrantCommandPermission();
        Assert.IsFalse(pipeline.TryAcceptOperand(true));
        Assert.IsTrue(pipeline.CommandPermission);
        pipeline.Commands.TryReceive(3);
        Assert.IsTrue(pipeline.TryAcceptOperand(true));
        Assert.IsFalse(pipeline.TryAcceptOperand(true));
        Assert.AreEqual(3U, pipeline.PreparedCommand);
    }

    [TestMethod]
    public void TimelineSeparatesPreparationCompletionAndStart()
    {
        var pipeline = new ArithmeticPipelineControl();
        var timeline = new HardwareTimeline();
        var log = new List<string>();
        pipeline.Commands.TryReceive(1);
        pipeline.Commands.TryReceive(2);
        pipeline.GrantCommandPermission();
        pipeline.TryAcceptOperand(true);
        pipeline.TryStartOperation();
        timeline.ScheduleAt(new(50), () => { pipeline.GrantCommandPermission(); log.Add("RPK"); });
        timeline.ScheduleAt(new(60), () => { Assert.IsTrue(pipeline.TryAcceptOperand(true)); log.Add("PVR"); });
        timeline.ScheduleAt(new(80), () => Assert.IsFalse(pipeline.TryStartOperation()));
        timeline.ScheduleAt(new(100), () => { Assert.AreEqual(1U, pipeline.CompleteOperation()); log.Add("IZOP"); });
        timeline.ScheduleAt(new(100), () => { Assert.IsTrue(pipeline.TryStartOperation()); log.Add("SPOP"); });
        timeline.AdvanceTo(new(100));
        CollectionAssert.AreEqual(new[] { "RPK", "PVR", "IZOP", "SPOP" }, log);
        Assert.AreEqual(2U, pipeline.ActiveCommand);
        // Synthetic instants verify the source-established dependency order only.
    }
}
