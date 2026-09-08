using System.Runtime.CompilerServices;

// Монолит (besm6) и тестовые сборки обращаются к internal-состоянию
// процессора (регистры A/Y/K, флага правого полуслова, canon-trace).
[assembly: InternalsVisibleTo("besm6")]
[assembly: InternalsVisibleTo("Besm6.Tests")]
[assembly: InternalsVisibleTo("Besm6.Processor.Tests")]
[assembly: InternalsVisibleTo("Besm6.Runtime")]
