using System.Reflection;
using Besm6.Core;

namespace Besm6.Tests
{
    [TestClass]
    public sealed class ProcessorDependencyTests
    {
        [TestMethod]
        public void ProcessorAssembly_HasNoHostIoDependencies()
        {
            Assembly assembly = typeof(Processor).Assembly;
            string[] references = assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name ?? string.Empty)
                .ToArray();

            CollectionAssert.DoesNotContain(references, "System.Console");
            CollectionAssert.DoesNotContain(references, "System.IO.FileSystem");
            CollectionAssert.DoesNotContain(references, "Besm6.Runtime");
            CollectionAssert.DoesNotContain(references, "Besm6.Cli");
            CollectionAssert.DoesNotContain(references, "Besm6.Tui");

            string[] ioFields = assembly.GetTypes()
                .SelectMany(type => type.GetFields(
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic))
                .Where(field => field.FieldType.Namespace?.StartsWith("System.IO", StringComparison.Ordinal) == true)
                .Select(field => $"{field.DeclaringType?.FullName}.{field.Name}")
                .ToArray();

            Assert.AreEqual(0, ioFields.Length,
                "Processor assembly хранит host I/O objects: {0}", string.Join(", ", ioFields));
        }

        [TestMethod]
        public void StopReason_BelongsToProcessorAssembly()
        {
            Assert.AreEqual(
                typeof(Processor).Assembly.GetName().Name,
                typeof(StopReason).Assembly.GetName().Name);
        }
    }
}
