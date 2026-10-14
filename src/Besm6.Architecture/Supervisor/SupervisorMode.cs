namespace Besm6.Architecture.Supervisor;

/// <summary>TO-8: supervisor is the union of extracode and interrupt modes.</summary>
[Flags]
public enum SupervisorMode { Mathematical = 0, Extracode = 1, Interrupt = 2 }
