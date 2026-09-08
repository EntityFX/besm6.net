using System;
using System.Linq;
using System.Reflection;

namespace Besm6.Architecture.Tests
{
    /// <summary>
    /// РђСЂС…РёС‚РµРєС‚СѓСЂРЅС‹Рµ РіСЂР°РЅРёС†С‹ С„Р°Р·С‹ 1 (plans/refactor.md):
    /// - Besm6.Architecture РЅРµ СЃСЃС‹Р»Р°РµС‚СЃСЏ РЅР° РґСЂСѓРіРёРµ BESM-6 СЃР±РѕСЂРєРё;
    /// - Besm6.Processor СЃСЃС‹Р»Р°РµС‚СЃСЏ С‚РѕР»СЊРєРѕ РЅР° Besm6.Architecture
    ///   (Рё РЅР° СЃРёСЃС‚РµРјРЅС‹Рµ СЃР±РѕСЂРєРё .NET), Р° РЅРµ РЅР° Loader, CLI РёР»Рё TUI;
    /// - Besm6.Processor РЅРµ СЃРѕРґРµСЂР¶РёС‚ С‚РёРїРѕРІ СѓСЃС‚СЂРѕР№СЃС‚РІ Рё Р·Р°РіСЂСѓР·С‡РёРєР°;
    /// - РјРѕРЅРѕР»РёС‚РЅС‹Р№ executable СЃСЃС‹Р»Р°РµС‚СЃСЏ РЅР° РѕР±Рµ РІС‹РЅРµСЃРµРЅРЅС‹Рµ СЃР±РѕСЂРєРё.
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
        public void Besm6Executable_References_Both_Extracted_Assemblies()
        {
            var mono = Assembly.GetAssembly(typeof(Besm6.Runtime.MachineCore));
            Assert.IsNotNull(mono, "РњРѕРЅРѕР»РёС‚РЅС‹Р№ executable (besm6) РґРѕР»Р¶РµРЅ РѕСЃС‚Р°РІР°С‚СЊСЃСЏ СЃР±РѕСЂРєРѕР№ СЂРµС€РµРЅРёСЏ.");

            string[] refs = mono.GetReferencedAssemblies().Select(d => d.Name ?? string.Empty).ToArray();
            CollectionAssert.Contains(refs, "Besm6.Architecture",
                "besm6 РґРѕР»Р¶РµРЅ СЃСЃС‹Р»Р°С‚СЊСЃСЏ РЅР° Besm6.Architecture.");
            CollectionAssert.Contains(refs, "Besm6.Processor",
                "besm6 РґРѕР»Р¶РµРЅ СЃСЃС‹Р»Р°С‚СЊСЃСЏ РЅР° Besm6.Processor.");
        }
    }
}
