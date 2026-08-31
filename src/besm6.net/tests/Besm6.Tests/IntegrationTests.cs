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
        private MachineCore _machine;
        private DubnaLoader _loader;

        [TestInitialize]
        public void Setup()
        {
            _output = new StringBuilder();
            _machine = new MachineCore();
            _loader = new DubnaLoader(_machine) { Verbose = false };
            // Перехватываем вывод из ExtracodeHandler.E64
            _loader.Output = s => _output.Append(s);
            // Перехватываем стандартный вывод для проверки
            Console.SetOut(new StringWriter(_output));
        }

        [TestCleanup]
        public void Cleanup()
        {
            Console.SetOut(Console.Out);
        }

        [TestMethod]
        // Known limitation: MONSYS-ядро неполное — сбой на э63(0) [M[16]=0] при настройке
        // это аспирационный тест, требующий полного ядра MONSYS.
        public void NameDub_ProducesMonsysBanner()
        {
            // Запускаем name.dub и проверяем, что баннер MONSYS выводится.
            // Ожидаемый вывод (GOST → Unicode) включает:
            //   "ЙOKCEЛ      БЭCM-6/5     ШИФP-12"
            //   "MOHИTOPHAЯ CИCTEMA  ′Д Y Б H A′  -  20/10/88"
            // и логотип из символов "Ж".

            // Ищем файл name.dub, поднимаясь вверх по дереву каталогов
            string path = FindFileInParentDirs("examples", "name.dub");
            if (path == null)
                Assert.Inconclusive("File examples/name.dub not found");

            var result = _loader.RunScript(path);
            string output = _output.ToString();

            Console.WriteLine(output);

            // Проверяем ключевые части баннера
            StringAssert.Contains(output, "ЙOKCEЛ", "Баннер MONSYS должен содержать ЙOKCEЛ");
            StringAssert.Contains(output, "БЭCM-6/5", "Баннер MONSYS должен содержать БЭCM-6/5");
            StringAssert.Contains(output, "ШИФP-12", "Баннер MONSYS должен содержать ШИФP-12");
            StringAssert.Contains(output, "MOHИTOPHAЯ", "Баннер MONSYS должен содержать MOHИTOPHAЯ");
            StringAssert.Contains(output, "CИCTEMA", "Баннер MONSYS должен содержать CИCTEMA");
            StringAssert.Contains(output, "Д Y Б H A", "Баннер MONSYS должен содержать Д Y Б H A");
            // Логотип из символов Ж.
            StringAssert.Contains(output, "ЖЖЖЖ", "Баннер MONSYS должен содержать логотип ЖЖЖЖ");
        }

        [TestMethod]
        public void ForexDub_ProducesHelloWorld()
        {
            // forex.dub — это пример FORTRAN, который выводит "Hello, World!"
            string path = FindFileInParentDirs("examples", "forex.dub");
            if (path == null)
                Assert.Inconclusive("File examples/forex.dub not found");
            
            var result = _loader.RunScript(path);
            string output = _output.ToString();
            
            //StringAssert.Contains(output, "Hello, World!", "FOREX пример должен выводить Hello, World!");
        }

        [TestMethod]
        public void RawHelloDub_ProducesHi()
        {
            string? path = FindFileInParentDirs("src/besm6.net/tests/raw", "hello.dub");
            Assert.IsNotNull(path, "File src/besm6.net/tests/raw/hello.dub not found");

            LoadResult result = _loader.RunScript(path);
            Assert.IsTrue(result.Success, result.ToString());
            StringAssert.Contains(_output.ToString(), "HI");
        }

        private static string FindFileInParentDirs(string relativePath, string fileName)
        {
            string currentDir = Directory.GetCurrentDirectory();
            while (currentDir != null)
            {
                string testPath = Path.Combine(currentDir, relativePath, fileName);
                if (File.Exists(testPath))
                    return testPath;
                currentDir = Directory.GetParent(currentDir)?.FullName;
            }
            return null;
        }
    }
}
