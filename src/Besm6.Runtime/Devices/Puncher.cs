using Besm6.Processor.Memory;
using Besm6.Architecture.Memory;
using Besm6.Runtime.Encoding;
using System;
using System.IO;
using System.Text;
namespace Besm6.Runtime.Devices
{
    public enum PuncherTextFormat { Braille, Grid, Ascii }
    /// <summary>
    /// Вывод перфокарт (порт dubna/puncher.cpp).
    /// Каждая перфокарта занимает 24 слова (144 байта). Диапазон слов
    /// разбивается на целое число карточек; каждая карточка выдаётся в
    /// текстовом формате (punch.out, по умолчанию Braille), а если это "стандартный массив" (колонка
    /// 0 == 01200) или COSY-массив (колонка 0 == 05000) — дополнительно в
    /// stdarray.out / cosy.out.
    /// </summary>
    public sealed class Puncher
    {
        private readonly IMemory _memory;
        private readonly string _outputDir;
        private StreamWriter? _braille;
        private StreamWriter? _stdarray;
        private StreamWriter? _cosy;
        private readonly StringBuilder _cosyString = new();
        /// <summary>Text representation written to punch.out; the default preserves the historical output.</summary>
        public PuncherTextFormat TextFormat { get; set; } = PuncherTextFormat.Braille;
        /// <summary>Immutable physical card, independent of the selected text representation.</summary>
        public event Action<PunchCardImage>? CardPunched;

        /// <summary>
        /// Создаёт puncher. <paramref name="outputDir"/> — каталог для punch.out /
        /// </summary>
        public Puncher(IMemory memory, string? outputDir = null)
        {
            _memory = memory;
            _outputDir = outputDir ?? Directory.GetCurrentDirectory();
        }

        /// <summary>Закрывает все файлы вывода.</summary>
        public void Finish()
        {
            if (_braille != null) { _braille.Flush(); _braille.Dispose(); _braille = null; }
            if (_stdarray != null) { _stdarray.Flush(); _stdarray.Dispose(); _stdarray = null; }
            if (_cosy != null) { _cosy.Flush(); _cosy.Dispose(); _cosy = null; }
        }

        /// <summary>Make punch output visible to observers without closing or truncating the files.</summary>
        public void Flush()
        {
            _braille?.Flush();
            _stdarray?.Flush();
            _cosy?.Flush();
        }

        /// <summary>
        /// Прошивает диапазон слов [startAddr, endAddr) как перфокарты.
        /// Диапазон обязан содержать целое число карточек (24 слова).
        /// </summary>
        public void Punch(int startAddr, int endAddr)
        {
            int a = startAddr;
            while (a < endAddr)
            {
                var bp = new BytePointer(_memory, (uint)(a & 0x7FFF));
                byte[] buf = new byte[144];
                for (int i = 0; i < 144; i++) buf[i] = bp.Get();

                var card = PunchCardImage.FromMemoryImage(buf);
                PunchText(card);
                CardPunched?.Invoke(card);

                var columns = new ushort[80];
                Transpose(buf, columns);
                switch (columns[0])
                {
                    case 0x280: PunchStdarray(columns); break; // 01200 oct — стандартный массив
                    case 0xA00: PunchCosy(columns); break;     // 05000 oct — COSY-массив
                }
                a += 24;
            }
        }

        /// <summary>Write one card in the selected text format.</summary>
        private void PunchText(PunchCardImage card)
        {
            if (_braille == null)
            {
                _braille = OpenWriter("punch.out");
                if (_braille == null) return;
            }
            _braille.Write(TextFormat switch
            {
                PuncherTextFormat.Braille => PunchCardRenderer.RenderBraille(card),
                PuncherTextFormat.Grid => PunchCardRenderer.RenderGrid(card, PunchCardEncoding.Ibm),
                PuncherTextFormat.Ascii => PunchCardRenderer.RenderGrid(card, PunchCardEncoding.Ibm, ascii: true),
                _ => throw new InvalidOperationException("Unknown puncher text format.")
            });
        }

        /// <summary>Транспонирует образ карточки в колонки (12 бит на колонку).</summary>
        private static void Transpose(byte[] buf, ushort[] columns)
        {
            Array.Clear(columns, 0, columns.Length);
            for (int col = 0; col < 80; col++)
            {
                for (int line = 0; line < 12; line++)
                {
                    int idx = 1 + 12 * line + (col >= 40 ? 1 : 0) + col / 8;
                    int bit = (buf[idx] >> (7 - col % 8)) & 1;
                    if (bit != 0)
                        columns[col] |= (ushort)(1 << line);
                }
            }
        }

        /// <summary>Вывод "стандартного массива" в восьмеричном формате.</summary>
        private void PunchStdarray(ushort[] columns)
        {
            if (columns[1] == 0) return; // титульная карточка
            if (_stdarray == null)
            {
                _stdarray = OpenWriter("stdarray.out");
                if (_stdarray == null) return;
            }
            _stdarray!.Write("`77761 ");
            for (int col = 76; col < 80; col++)
            {
                _stdarray.Write(CosyCodec.TextToUnicode((byte)(columns[col] >> 6)));
                _stdarray.Write(CosyCodec.TextToUnicode((byte)(columns[col] & 0x7F)));
            }
            _stdarray.Write(' ');
            _stdarray.Write(columns[1]);
            _stdarray.Write('\n');

            for (int col = 4; col < 75; col += 9)
            {
                for (int i = 0; i < 8; i++)
                {
                    if (i % 4 == 0) _stdarray.Write('`');
                    _stdarray.Write(Convert.ToString(columns[col + i], 8).PadLeft(4, '0'));
                    if (i % 4 == 3) _stdarray.Write('\n');
                }
            }
        }

        /// <summary>Вывод COSY-массива (текст + заголовок).</summary>
        private void PunchCosy(ushort[] columns)
        {
            if (_cosy == null)
            {
                _cosy = OpenWriter("cosy.out");
                if (_cosy == null) return;
            }
            if (columns[1] == 0)
            {
                // Титульная карточка: имя массива в фигурных скобках.
                _cosyString.Clear();
                _cosy!.Write('{');
                for (int col = 76; col < 80; col++)
                {
                    _cosy.Write(CosyCodec.TextToUnicode((byte)(columns[col] >> 6)));
                    _cosy.Write(CosyCodec.TextToUnicode((byte)(columns[col] & 0x7F)));
                }
                _cosy.Write("}\n");
                return;
            }

            for (int col = 4; col < 75; col += 9)
            {
                long w = 0;
                for (int i = 0; i < 8; i++)
                {
                    if (i % 4 == 0) w = 0;
                    w = (w << 12) | columns[col + i];
                    if (i % 4 == 3)
                    {
                        for (int j = 40; j >= 0; j -= 8)
                        {
                            byte c = (byte)((w >> j) & 0xFF);
                            if (c == '\n')
                            {
                                while (_cosyString.Length > 0 && _cosyString[_cosyString.Length - 1] == ' ')
                                    _cosyString.Remove(_cosyString.Length - 1, 1);
                                for (int p = 0; p < _cosyString.Length; p++)
                                    _cosy!.Write(CosyCodec.Koi7ToUnicode((byte)_cosyString[p]));
                                _cosy!.Write('\n');
                                _cosyString.Clear();
                                break; // выравнивание по слову
                            }
                            else if (c < 0x80)
                            {
                                _cosyString.Append((char)c);
                            }
                            else
                            {
                                for (int k = 0; k < (c - 0x80); k++) _cosyString.Append(' ');
                            }
                        }
                    }
                }
            }
        }

        private StreamWriter? OpenWriter(string fileName)
        {
            try
            {
                return new StreamWriter(Path.Combine(_outputDir, fileName), false, new UTF8Encoding(false));
            }
            catch
            {
                return null;
            }
        }
    }
}
