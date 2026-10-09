namespace Besm6.Core;

/// <summary>Execution semantics; host pacing remains an independent setting.</summary>
public enum ProcessorProfile { Dubna, Supervisor }

/// <summary>TO-8: supervisor is the union of extracode and interrupt modes.</summary>
[Flags]
public enum SupervisorMode { Mathematical = 0, Extracode = 1, Interrupt = 2 }
