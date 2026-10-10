namespace Besm6.Runtime.Timing;

/// <summary>
/// АУ — арифметическое устройство.
/// Logical ordering of the TO-3 §3.2 signals: operand acceptance PVR needs
/// RPK and a ready operand; SPОP needs preparation and previous IZOP.
/// RPK may arrive before IZOP. The caller supplies actual signal instants;
/// this component does not derive pulse lengths, instruction costs or data.
/// It is not an alternative instruction interpreter.
/// </summary>
public sealed class ArithmeticPipelineControl
{
    public ArithmeticCommandBuffer Commands { get; } = new();
    public uint? ActiveCommand { get; private set; }
    public uint? PreparedCommand { get; private set; }
    public bool CommandPermission { get; private set; }

    /// <summary>RPK indication; initial permission is not inferred from construction.</summary>
    public void GrantCommandPermission() => CommandPermission = true;

    /// <summary>PVR: frees the arithmetic command register after both prerequisites hold.</summary>
    public bool TryAcceptOperand(bool operandReady)
    {
        if (!CommandPermission || !operandReady || PreparedCommand.HasValue ||
            !Commands.TryPeek(out _)) return false;
        PreparedCommand = Commands.AcceptOperand();
        CommandPermission = false;
        return true;
    }

    /// <summary>SPОP: starts a prepared operation after completion of its predecessor.</summary>
    public bool TryStartOperation()
    {
        if (ActiveCommand.HasValue || PreparedCommand is not uint command) return false;
        ActiveCommand = command;
        PreparedCommand = null;
        return true;
    }

    /// <summary>IZOP exists independently of whether the successor has been prepared.</summary>
    public uint CompleteOperation()
    {
        if (ActiveCommand is not uint command)
            throw new InvalidOperationException("IZOP requires an active arithmetic operation.");
        ActiveCommand = null;
        return command;
    }

    internal void ApplyGeneralClearSignal()
    {
        Commands.ApplyGeneralClearSignal();
        ActiveCommand = PreparedCommand = null;
        CommandPermission = false; // RPK must be supplied by its documented chain.
    }
}
