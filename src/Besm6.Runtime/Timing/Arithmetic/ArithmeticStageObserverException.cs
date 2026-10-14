namespace Besm6.Runtime.Timing.Arithmetic;

internal sealed class ArithmeticStageObserverException(ArithmeticStageTransition transition, Exception cause)
    : Exception("Arithmetic stage observer failed after the transition.", cause)
{
    internal ArithmeticStageTransition Transition { get; } = transition;
}
