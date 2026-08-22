using System;
using System.IO;
using System.Text;
using Besm6.Core;
using Besm6.Loader;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Besm6.Tests
{
    /// <summary>
    /// Интеграционные тесты: запуск .dub файлов и проверка вывода.
    /// </summary>
    [TestClass]
    public class IntegrationTests
    {
        private StringBuilder _output;
        private Machine _machine;
        private DubnaLoader _loader;

        [TestInitialize]
        public void Setup()
        {
            _output = new StringBuilder();
            _machine = new Machine();
            _loader = new DubnaLoader(_machine);
            // Перехватываем стандартный вывод для проверки
            Console.SetOut(new StringWriter(_output));
        }

        [TestCleanup]
        public void Cleanup()
        {
            Console.SetOut(Console.Out);
        }

        [TestMethod]
        public void NameDub_ProducesMonsysBanner()
        {
            // Запускаем name.dub и проверяем, что баннер MONSYS выводится.
            // Ожидаемый вывод включает: "ЙOKCEЛ      БЭCM-6/5     ШИФP-12"
            // и логотип "Ж" (ЖЖЖ и т.д.)
            
            var result = _loader.RunScript("examples/name.dub");
            string output = _output.ToString();
            
            // Проверяем ключевые части баннера
            StringAssert.Contains(output, "ЙOKCEЛ", "Баннер MONSYS должен содержать ЙOKCEЛ");
            StringAssert.Contains(output, "БЭCM-6/5", "Баннер MONSYS должен содержать БЭCM-6/5");
            StringAssert.Contains(output, "ШИФP-12", "Баннер MONSYS должен содержать ШИФP-12");
            StringAssert.Contains(output, "МОНИТОРНАЯ", "Баннер MONSYS должен содержать МОНИТОРНАЯ");
            StringAssert.Contains(output, "СИСТЕМА", "Баннер MONSYS должен содержать СИСТЕМА");
            StringAssert.Contains(output, "ДУБНА", "Баннер MONSYS должен содержать ДУБНА");
        }

        [TestMethod]
        public void ForexDub_ProducesHelloWorld()
        {
            // forex.dub — это пример FORTRAN, который выводит "Hello, World!"
            var result = _loader.RunScript("examples/forex.dub");
            string output = _output.ToString();
            
            StringAssert.Contains(output, "Hello, World!", "FOREX пример должен выводить Hello, World!");
        }

        [TestMethod]
        public void RawHelloDub_ProducesHelloWorld()
        {
            // tests/raw/hello.dub — это простой пример, который выводит "Hello, World!"
            var result = _loader.RunScript("tests/raw/hello.dub");
            string output = _output.ToString();
            
            StringAssert.Contains(output, "Hello, World!", "Raw hello пример должен выводить Hello, World!");
        }
    }
}
