using System;
using System.Text;

namespace Besm6.Runtime
{
    /// <summary>
    /// E64 numeric formatters (Itm, Octal, Hex, Real).
    /// </summary>
    public sealed partial class ExtracodeHandler
    {

        // --- ITM text ---

        private int E64PrintItm(int startAddr, int endAddr)
        {
            int wordAddr = startAddr;
            int byteIdx = 0;
            byte lastCh = G_SPACE;

            while (wordAddr != 0)
            {
                if (endAddr != 0 && wordAddr == endAddr + 1)
                    return wordAddr;

                if (_e64Position == E64_LINE_WIDTH)
                {
                    if (endAddr == 0)
                    {
                        if (byteIdx > 0) wordAddr++;
                        return wordAddr;
                    }
                    E64EmitLine();
                }

                long word = MemRead(wordAddr);
                byte ch = (byte)((word >> (40 - byteIdx * 8)) & 0xFF);

                switch (ch)
                {
                    case 0x60: // 0140 - end of information
                        if (byteIdx > 0) wordAddr++;
                        return wordAddr;

                    case 0x20: // 0040 - blank
                        E64PutChar(G_SPACE);
                        break;

                    case 0x7B: // 0173 - repeat last symbol
                    {
                        int nextIdx = byteIdx + 1;
                        if (nextIdx >= 6) nextIdx = 0;
                        byte count = (byte)((word >> (40 - nextIdx * 8)) & 0xFF);
                        byteIdx += 2;
                        if (byteIdx >= 6) { wordAddr++; byteIdx -= 6; }

                        if (count == 0x20) // 0040
                        {
                            Array.Fill(_e64Line, lastCh);
                            E64EmitLine();
                        }
                        else
                        {
                            int n = count & 0x0F;
                            for (int i = 0; i < n; i++)
                                E64PutChar(lastCh);
                        }
                        break;
                    }

                    default:
                        lastCh = ItmToGost[ch];
                        E64PutChar(lastCh);
                        byteIdx++;
                        if (byteIdx >= 6) { wordAddr++; byteIdx = 0; }
                        break;
                }
            }
            return 0;
        }

        // --- Octal ---

        private int E64PrintOctal(int startAddr, int endAddr,
            int digits, int width, int repeat)
        {
            if (digits > 16) digits = 16;

            while (startAddr != 0)
            {
                if (endAddr != 0 && startAddr == endAddr + 1)
                    return startAddr;

                if (_e64Position >= E64_LINE_WIDTH)
                {
                    if (endAddr == 0) return 0;
                    return startAddr;
                }

                long word = MemRead(startAddr);
                startAddr++;

                word <<= (64 - digits * 3);
                for (int i = 0; i < digits; i++)
                {
                    int d = (int)((word >> 61) & 7);
                    E64PutChar(G_0 + (byte)d);
                    word <<= 3;
                }

                if (repeat == 0)
                    return startAddr;

                repeat--;
                if (width > digits)
                    _e64Position += width - digits;
            }
            return 0;
        }

        // --- Hex ---

        private static readonly byte[] HexGostDigits =
        {
            G_0, G_1, G_2, G_3, G_4, G_5, G_6, G_7,
            G_8, G_9, G_A, G_B, G_C, G_D, G_E, G_F
        };

        private int E64PrintHex(int startAddr, int endAddr,
            int digits, int width, int repeat)
        {
            if (digits > 12) digits = 12;

            while (startAddr != 0)
            {
                if (endAddr != 0 && startAddr == endAddr + 1)
                    return startAddr;

                if (_e64Position >= E64_LINE_WIDTH)
                {
                    if (endAddr == 0) return 0;
                    return startAddr;
                }

                long word = MemRead(startAddr);
                startAddr++;

                word <<= (64 - digits * 4);
                for (int i = 0; i < digits; i++)
                {
                    int h = (int)((word >> 60) & 15);
                    E64PutChar(HexGostDigits[h]);
                    word <<= 4;
                }

                if (repeat == 0)
                {
                    if (endAddr != 0 && startAddr <= endAddr)
                    {
                        E64EmitLine();
                        repeat = 1;
                    }
                    else
                    {
                        return startAddr;
                    }
                }
                repeat--;
                if (width > digits)
                    _e64Position += width - digits;
            }
            return 0;
        }

        // --- Real number ---

        private int E64PrintReal(int startAddr, int endAddr,
            int digits, int width, int repeat)
        {
            if (digits > 20) digits = 20;
            if (digits < 4) digits = 4;

            while (startAddr != 0)
            {
                if (endAddr != 0 && startAddr == endAddr + 1)
                    return startAddr;

                if (_e64Position >= E64_LINE_WIDTH)
                {
                    if (endAddr == 0) return 0;
                    return startAddr;
                }

                long word = MemRead(startAddr);
                startAddr++;

                bool negative = (word & (1L << 40)) != 0;
                double value = 0;
                int exponent = 0;

                if (word != 0 && word != (1L << 40))
                {
                    value = Besm6Math.Besm6ToDouble((ulong)word);
                    if (value < 0) value = -value;
                    value = RealExponent(value, ref exponent);
                }

                E64PutChar(G_SPACE);
                E64PutChar(negative ? G_MINUS : G_PLUS);

                value += 0.5 / Math.Pow(10.0, digits - 4);
                if (value >= 1)
                {
                    value /= 10;
                    exponent++;
                }

                for (int i = 0; i < digits - 4; i++)
                {
                    value *= 10;
                    int d = (int)value;
                    E64PutChar(G_0 + (byte)d);
                    value -= d;
                }

                E64PutChar(G_LOWER_TEN);
                if (exponent >= 0)
                    E64PutChar(G_PLUS);
                else
                {
                    E64PutChar(G_MINUS);
                    exponent = -exponent;
                }
                E64PutChar(G_0 + (byte)(exponent / 10));
                E64PutChar(G_0 + (byte)(exponent % 10));

                if (repeat == 0)
                {
                    if (endAddr != 0 && startAddr <= endAddr)
                    {
                        E64EmitLine();
                        repeat = 1;
                    }
                    else
                    {
                        return startAddr;
                    }
                }
                repeat--;
                if (width > digits + 2)
                    _e64Position += width - digits - 2;
            }
            return 0;
        }

        private static double RealExponent(double value, ref int exponent)
        {
            exponent = 0;
            if (value <= 0) return 0;
            while (value >= 1000000) { exponent += 6; value /= 1000000; }
            while (value >= 1) { exponent++; value /= 10; }
            while (value < 0.0000001) { exponent -= 6; value *= 1000000; }
            while (value < 0.1) { exponent--; value *= 10; }
            return value;
        }
    }
}
