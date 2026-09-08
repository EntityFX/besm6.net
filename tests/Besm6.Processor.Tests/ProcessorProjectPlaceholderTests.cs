namespace Besm6.Processor.Tests
{
    /// <summary>
    /// Плейсхолдер: проектная заглушка, пока процессорные тесты
    /// не перенесены сюда из tests/Besm6.Tests (plans/refactor.md, Task 4).
    /// </summary>
    [TestClass]
    public sealed class ProcessorProjectPlaceholderTests
    {
        [TestMethod]
        public void Processor_Test_Project_Is_Built()
        {
            // Сборка тестового проекта должна идентифицироваться как Besm6.Processor.Tests.
            var asm = typeof(ProcessorProjectPlaceholderTests).Assembly;
            Assert.AreEqual("Besm6.Processor.Tests", asm.GetName().Name);
        }
    }
}
