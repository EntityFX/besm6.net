using System.Text;

namespace Besm6.Core
{
    /// <summary>
    /// Графопостроитель (порт dubna/plotter.h).
    /// Накопление байт для Watanabe WX4675, Calcomp и Tektronix.
    /// Конвертация в SVG в C#-порте опущена (не влияет на работу экстракодов),
    /// но накопленные данные доступны для тестов.
    /// </summary>
    public sealed class Plotter
    {
        private readonly StringBuilder _watanabe = new();
        private readonly StringBuilder _calcomp = new();
        private readonly StringBuilder _tektronix = new();

        /// <summary>Номер текущей страницы, начиная с 1. 0 — нумерация выключена (одна страница).</summary>
        public int PageNumber { get; private set; }

        /// <summary>Накопленные байты Watanabe WX4675.</summary>
        public string Watanabe => _watanabe.ToString();

        /// <summary>Накопленные байты Calcomp.</summary>
        public string Calcomp => _calcomp.ToString();

        /// <summary>Накопленные байты Tektronix.</summary>
        public string Tektronix => _tektronix.ToString();

        /// <summary>Передать байт плоттеру Watanabe WX4675.</summary>
        public void WatanabePutCh(char ch) => _watanabe.Append(ch);

        /// <summary>Передать байт плоттеру Calcomp.</summary>
        public void CalcompPutCh(char ch) => _calcomp.Append(ch);

        /// <summary>Передать байт плоттеру Tektronix.</summary>
        public void TektronixPutCh(char ch) => _tektronix.Append(ch);

        /// <summary>Завершить текущую страницу и начать новую (порт Plotter::change_page).</summary>
        public void ChangePage(bool keepTemporaryFiles = false) => PageNumber++;

        /// <summary>Сохранить выходные файлы (в C#-порте — no-op).</summary>
        public void Finish(bool keepTemporaryFiles = false)
        {
        }
    }
}