using System;
using System.Linq;
using System.Reflection;

namespace Besm6.Architecture.Tests
{
    /// <summary>
    /// Архитектурные границы фазы 1 (plans/refactor.md):
    /// - Besm6.Architecture не ссылается на другие BESM-6 сборки;
    /// - Besm6.Processor ссылается только на Besm6.Architecture
    ///   (и на системные сборки .NET), а не на Loader, CLI или TUI;
    /// - Besm6.Processor не содержит типов устройств и загрузчика;
    /// - монолитный executable ссылается на обе вынесенные сборки.
    /// </summary>
    [TestClass]
    public sealed class AssemblyBoundaryTests
    {
        private static Assembly? TryLoad(string assemblyName)
        {
            try
            {
                return Assembly.Load(new AssemblyName(assemblyName));
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (BadImageFormatException)
            {
                return null;
            }
        }

        private static bool IsBesm6Family(string? assemblyName)
        {
            return assemblyName is not null
                && (assemblyName.Equals("besm6", StringComparison.OrdinalIgnoreCase)
                    || assemblyName.StartsWith("Besm6.", StringComparison.OrdinalIgnoreCase));
        }

        [TestMethod]
        public void Architecture_Assembly_Exists_And_References_No_Besm6_Families()
        {
            var asm = TryLoad("Besm6.Architecture");
            Assert.IsNotNull(asm, "Сборка Besm6.Architecture не найдена (src/Besm6.Architecture ещё не создана).");

            foreach (var dep in asm.GetReferencedAssemblies())
            {
                Assert.IsFalse(IsBesm6Family(dep.Name),
                    $"Besm6.Architecture не должна ссылаться на другие BESM-6 сборки: {dep.Name}");
            }
        }

        [TestMethod]
        public void Processor_Assembly_References_Only_Architecture()
        {
            var asm = TryLoad("Besm6.Processor");
            Assert.IsNotNull(asm, "Сборка Besm6.Processor не найдена (src/Besm6.Processor ещё не создана).");

            foreach (var dep in asm.GetReferencedAssemblies())
            {
                if (!IsBesm6Family(dep.Name))
                    continue; // System.* и т.п. разрешены.
                Assert.AreEqual("Besm6.Architecture", dep.Name,
                    $"Besm6.Processor может ссылаться только на Besm6.Architecture, найден {dep.Name}");
            }
        }

        [TestMethod]
        public void Processor_Assembly_Contains_No_DeviceOrLoader_Types()
        {
            var asm = TryLoad("Besm6.Processor");
            Assert.IsNotNull(asm, "Сборка Besm6.Processor не найдена (src/Besm6.Processor ещё не создана).");

            Type[] types = asm.GetTypes();
            string[] forbidden = types
                .Where(t => t.Name.Contains("Device")
                    || t.Name.Contains("Loader")
                    || t.Name.Contains("Tape")
                    || t.Name.Contains("Puncher")
                    || t.Name.Contains("Plotter")
                    || t.Name.Contains("Drum")
                    || t.Name.Contains("Disk")
                    || t.Name.Contains("Teletype"))
                .Select(t => t.FullName ?? t.Name)
                .ToArray();

            Assert.AreEqual(0, forbidden.Length,
                "Besm6.Processor не должна содержать типов устройств и загрузчика: {0}", string.Join(", ", forbidden));
        }

        [TestMethod]
        public void Besm6Executable_References_Both_Extracted_Assemblies()
        {
            var mono = Assembly.GetAssembly(typeof(Besm6.Core.MachineCore));
            Assert.IsNotNull(mono, "Монолитный executable (besm6) должен оставаться сборкой решения.");

            string[] refs = mono.GetReferencedAssemblies().Select(d => d.Name ?? string.Empty).ToArray();
            CollectionAssert.Contains(refs, "Besm6.Architecture",
                "besm6 должен ссылаться на Besm6.Architecture.");
            CollectionAssert.Contains(refs, "Besm6.Processor",
                "besm6 должен ссылаться на Besm6.Processor.");
        }
    }
}
