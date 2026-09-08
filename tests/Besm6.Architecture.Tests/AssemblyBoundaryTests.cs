using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Besm6.Architecture.Tests
{
    /// <summary>
    /// Границы сборок этапа 1 (plans/refactor.md) и финальная модель (plans/SuperPlan.md):
    /// Architecture изолирована; Processor -> Architecture; Assembler -> Architecture+Processor;
    /// Runtime -> Architecture+Processor+Assembler (без CLI/TUI); CLI и TUI — независимые
    /// executables; граф зависимостей не содержит циклов.
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

        private static string[] Besm6Refs(Assembly? asm)
        {
            return (asm?.GetReferencedAssemblies() ?? Array.Empty<AssemblyName>())
                .Where(d => IsBesm6Family(d.Name))
                .Select(d => d.Name!)
                .OrderBy(n => n)
                .ToArray();
        }

        [TestMethod]
        public void Architecture_Assembly_Exists_And_References_No_Besm6_Families()
        {
            var asm = TryLoad("Besm6.Architecture");
            Assert.IsNotNull(asm, "РЎР±РѕСЂРєР° Besm6.Architecture РЅРµ РЅР°Р№РґРµРЅР° (src/Besm6.Architecture РµС‰С‘ РЅРµ СЃРѕР·РґР°РЅР°).");

            foreach (var dep in asm.GetReferencedAssemblies())
            {
                Assert.IsFalse(IsBesm6Family(dep.Name),
                    $"Besm6.Architecture РЅРµ РґРѕР»Р¶РЅР° СЃСЃС‹Р»Р°С‚СЊСЃСЏ РЅР° РґСЂСѓРіРёРµ BESM-6 СЃР±РѕСЂРєРё: {dep.Name}");
            }
        }

        [TestMethod]
        public void Architecture_Assembly_Contains_No_RuntimeDeviceTypes()
        {
            Assembly asm = typeof(Word48).Assembly;
            string[] forbidden = asm.GetTypes()
                .Where(type => type.Namespace?.Equals("Besm6.Core", StringComparison.Ordinal) == true
                    || type.Name.Contains("Device", StringComparison.Ordinal)
                    || type.Name.Contains("Loader", StringComparison.Ordinal)
                    || type.Name.Contains("Tape", StringComparison.Ordinal)
                    || type.Name.Contains("Disk", StringComparison.Ordinal))
                .Select(type => type.FullName ?? type.Name)
                .ToArray();

            Assert.AreEqual(0, forbidden.Length,
                "Architecture РґРѕР»Р¶РЅР° СЃРѕРґРµСЂР¶Р°С‚СЊ С‚РѕР»СЊРєРѕ РјРѕРґРµР»СЊ ISA: {0}", string.Join(", ", forbidden));
        }

        [TestMethod]
        public void ArchitectureConstants_ExcludeExecutionOnlyBit49()
        {
            string[] publicFields = typeof(ArchitectureConstants)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => field.Name)
                .ToArray();

            CollectionAssert.DoesNotContain(publicFields, "BIT49");
        }

        [TestMethod]
        public void Processor_Assembly_References_Only_Architecture()
        {
            var asm = TryLoad("Besm6.Processor");
            Assert.IsNotNull(asm, "РЎР±РѕСЂРєР° Besm6.Processor РЅРµ РЅР°Р№РґРµРЅР° (src/Besm6.Processor РµС‰С‘ РЅРµ СЃРѕР·РґР°РЅР°).");

            foreach (var dep in asm.GetReferencedAssemblies())
            {
                if (!IsBesm6Family(dep.Name))
                    continue; // System.* Рё С‚.Рї. СЂР°Р·СЂРµС€РµРЅС‹.
                Assert.AreEqual("Besm6.Architecture", dep.Name,
                    $"Besm6.Processor РјРѕР¶РµС‚ СЃСЃС‹Р»Р°С‚СЊСЃСЏ С‚РѕР»СЊРєРѕ РЅР° Besm6.Architecture, РЅР°Р№РґРµРЅ {dep.Name}");
            }
        }

        [TestMethod]
        public void Processor_Assembly_Contains_No_DeviceOrLoader_Types()
        {
            var asm = TryLoad("Besm6.Processor");
            Assert.IsNotNull(asm, "РЎР±РѕСЂРєР° Besm6.Processor РЅРµ РЅР°Р№РґРµРЅР° (src/Besm6.Processor РµС‰С‘ РЅРµ СЃРѕР·РґР°РЅР°).");

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
                "Besm6.Processor РЅРµ РґРѕР»Р¶РЅР° СЃРѕРґРµСЂР¶Р°С‚СЊ С‚РёРїРѕРІ СѓСЃС‚СЂРѕР№СЃС‚РІ Рё Р·Р°РіСЂСѓР·С‡РёРєР°: {0}", string.Join(", ", forbidden));
        }

        [TestMethod]
        public void Assembler_Assembly_References_Only_Architecture_And_Processor()
        {
            var asm = TryLoad("Besm6.Assembler");
            Assert.IsNotNull(asm, "Сборка Besm6.Assembler не найдена (src/Besm6.Assembler не существует).");

            string[] refs = Besm6Refs(asm);
            CollectionAssert.AreEquivalent(
                new[] { "Besm6.Architecture" }, refs,
                "Besm6.Assembler зависит только от Architecture, а не " + string.Join(", ", refs));
        }

        [TestMethod]
        public void Runtime_Assembly_References_Expected_Production_Assemblies()
        {
            var asm = TryLoad("Besm6.Runtime");
            Assert.IsNotNull(asm, "Сборка Besm6.Runtime не найдена (src/Besm6.Runtime не существует).");

            string[] refs = Besm6Refs(asm);
            CollectionAssert.Contains(refs, "Besm6.Architecture");
            CollectionAssert.Contains(refs, "Besm6.Processor");
            CollectionAssert.Contains(refs, "Besm6.Assembler");

            foreach (var forbidden in new[] { "Besm6.Cli", "Besm6.Tui" })
            {
                CollectionAssert.DoesNotContain(refs, forbidden,
                    "Besm6.Runtime не должна ссылаться на " + forbidden + ".");
            }
        }

        [TestMethod]
        public void Cli_Assembly_References_Runtime_And_Not_Tui()
        {
            var asm = TryLoad("besm6");
            Assert.IsNotNull(asm, "Executable-сборка besm6 (CLI) не найдена (src/Besm6.Cli не существует).");

            string[] refs = Besm6Refs(asm);
            CollectionAssert.Contains(refs, "Besm6.Runtime",
                "CLI должна ссылаться на Besm6.Runtime.");
            CollectionAssert.DoesNotContain(refs, "Besm6.Tui",
                "CLI не должна ссылаться на TUI.");
        }

        [TestMethod]
        public void Tui_Assembly_References_Runtime_And_Not_Cli()
        {
            var asm = TryLoad("besm6-tui");
            Assert.IsNotNull(asm, "Executable-сборка besm6-tui (TUI) не найдена (src/Besm6.Tui не существует).");

            string[] refs = Besm6Refs(asm);
            CollectionAssert.Contains(refs, "Besm6.Runtime",
                "TUI должна ссылаться на Besm6.Runtime.");
            CollectionAssert.DoesNotContain(refs, "besm6",
                "TUI не должна ссылаться на CLI-сборку.");
        }

        [TestMethod]
        public void Besm6_Assembly_Graph_Contains_No_Cycles()
        {
            string[] known =
            {
                "Besm6.Architecture", "Besm6.Processor", "Besm6.Assembler",
                "Besm6.Runtime", "besm6", "besm6-tui"
            };

            var direct = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in known)
            {
                var asm = TryLoad(name);
                Assert.IsNotNull(asm, "Сборка {0} не найдена.", name);
                direct[name] = Besm6Refs(asm!);
            }

            bool HasCycleFrom(string node, HashSet<string> stack)
            {
                if (stack.Contains(node))
                    return true;
                if (!direct.TryGetValue(node, out var deps))
                    return false;
                stack.Add(node);
                bool found = deps.Any(d => HasCycleFrom(d, stack));
                stack.Remove(node);
                return found;
            }

            bool hasCycle = known.Any(root => HasCycleFrom(root, new HashSet<string>()));
            Assert.IsFalse(hasCycle, "Граф зависимостей BESM-6 сборок содержит цикл.");
        }
    }
}
