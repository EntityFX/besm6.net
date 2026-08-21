using System;
using System.Text;
using Besm6.Core;

namespace Besm6.Loader
{
    /// <summary>
    /// E64: полный протокол вывода (порт dubna/e64.cpp).
    /// Line buffer (128 GOST-char), E64_Pointer, E64_Info, 6 форматов.
    /// </summary>
    public sealed partial class ExtracodeHandler
    {
        private const int E64_LINE_WIDTH = 128;

        // GOST constants (hex equivalents of octal)
        private const byte G_SPACE = 0x0F;       // 017
        private const byte G_0 = 0x00;
        private const byte G_1 = 0x01;
        private const byte G_2 = 0x02;
        private const byte G_3 = 0x03;
        private const byte G_4 = 0x04;
        private const byte G_5 = 0x05;
        private const byte G_6 = 0x06;
        private const byte G_7 = 0x07;
        private const byte G_8 = 0x08;           // 010
        private const byte G_9 = 0x09;           // 011
        private const byte G_PLUS = 0x0A;        // 012
        private const byte G_MINUS = 0x0B;       // 013
        private const byte G_LOWER_TEN = 0x10;   // 020
        private const byte G_A = 0x20;           // 040
        private const byte G_B = 0x21;           // 041
        private const byte G_C = 0x22;           // 042
        private const byte G_D = 0x23;           // 043
        private const byte G_E = 0x24;           // 044
        private const byte G_F = 0x25;           // 045
        private const byte G_G = 0x26;           // 046
        private const byte G_H = 0x27;           // 047
        private const byte G_I = 0x28;           // 050
        private const byte G_J = 0x29;           // 051
        private const byte G_K = 0x2A;           // 052
        private const byte G_L = 0x2B;           // 053
        private const byte G_M = 0x2C;           // 054
        private const byte G_N = 0x2D;           // 055
        private const byte G_O = 0x2E;           // 056
        private const byte G_P = 0x2F;           // 057
        private const byte G_Q = 0x30;           // 060
        private const byte G_R = 0x31;           // 061
        private const byte G_S = 0x32;           // 062
        private const byte G_T = 0x33;           // 063
        private const byte G_U = 0x34;           // 064
        private const byte G_V = 0x35;           // 065
        private const byte G_W = 0x36;           // 066
        private const byte G_X = 0x37;           // 067
        private const byte G_Y = 0x38;           // 070
        private const byte G_Z = 0x39;           // 071
        private const byte G_NULL_WIDTH = 0x63;  // 143
        private const byte G_END_OF_INFO = 0x7A; // 172
        private const byte G_SET_POS = 0x7B;     // 173
        private const byte G_EOLN = 0x7C;        // 174
        private const byte G_CR = 0x7D;          // 175
        private const byte G_SPACE2 = 0x7E;      // 176
        private const byte G_SET_POS2 = 0x80;    // 200
        private const byte G_NEWPAGE = 0x81;     // 201
        private const byte G_OVERPRINT = 0x8A;   // 212
        private const byte G_NEWLINE = 0x8C;     // 214
        private const byte G_SPACE3 = 0xA2;      // 242
        private const byte G_REPEAT = 0xB5;      // 265
        private const byte G_NULL_WIDTH2 = 0xE1; // 341
        private const byte G_EOF = 0xFF;         // 377
        private const byte G_DIA = 0x57;         // 127
        private const byte G_QUOTE_R = 0x1B;     // 033
        private const byte G_UNDERLINE = 0x5A;   // 132
        private const byte G_VLINE = 0x58;       // 130
        private const byte G_SEMI = 0x16;        // 026
        private const byte G_COMMA = 0x0D;       // 015
        private const byte G_DOT = 0x0E;         // 016
        private const byte G_OVERLINE = 0x4D;    // 115
        private const byte G_RPAREN = 0x13;      // 023
        private const byte G_LBRACKET = 0x17;    // 027
        private const byte G_GT = 0x1E;          // 036
        private const byte G_DEGREE = 0x5E;      // 136
        private const byte G_COLON = 0x1F;       // 037
        private const byte G_EQUALS = 0x15;      // 025
        private const byte G_V2 = 0x4A;          // 112
        private const byte G_PERCENT = 0x56;     // 126
        private const byte G_EXCL = 0x5B;        // 133
        private const byte G_LQUOTE = 0x1A;      // 032
        private const byte G_RBRACKET = 0x18;    // 030
        private const byte G_SLASH = 0x0C;       // 014
        private const byte G_LAND = 0x51;        // 121
        private const byte G_LT = 0x1D;          // 035
        private const byte G_LPAREN = 0x12;      // 022
        private const byte G_QUOTE = 0x5C;       // 134
        private const byte G_HARDSIGN = 0x5D;    // 135
        private const byte G_ARROW = 0x11;       // 021
        private const byte G_NOT = 0x53;         // 123
        private const byte G_NEQ = 0x1C;         // 034
        private const byte G_ASTERISK = 0x19;    // 031

        // ITM -> GOST table (port of itm_to_gost[256] from dubna/encoding.cpp)
        private static readonly byte[] ItmToGost =
        {
            // 000
            G_0, G_1, G_2, G_3, G_4, G_5, G_6, G_7,
            // 010
            G_8, G_9, 0, 0, 0, 0, 0, G_SPACE,
            // 020-030
            0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,
            // 040
            G_SPACE, G_QUOTE_R, G_DIA, G_UNDERLINE,
            G_VLINE, G_SEMI, G_COMMA, G_DOT,
            // 050
            G_OVERLINE, G_RPAREN, 0, G_LBRACKET,
            G_GT, G_DEGREE, G_COLON, G_EQUALS,
            // 060
            G_V2, G_PLUS, G_PERCENT, G_EXCL,
            G_LQUOTE, G_COLON, G_RBRACKET, G_SLASH,
            // 070
            G_MINUS, G_LAND, G_X, 0,
            G_LT, G_LQUOTE, G_LPAREN, 0,
            // 100-120
            0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0, 0,0,0,0,0,0,0,0,
            // 130
            0,0,0,0, 0,0, G_QUOTE, G_HARDSIGN,
            // 140
            0,0,0, G_ARROW,
            0, G_NOT, G_LT, G_GT,
            // 150
            G_MINUS, 0, G_NEQ, 0, 0,0,0,0,
            // 160
            0,0,0,0,0,0,0,0,
            // 170
            G_ASTERISK, 0, 0,0,0, G_E, 0,0,
            // 200
            0, G_T, 0, G_O,
            0, G_K, 0, G_P,
            // 210
            0, G_L, 0, G_U,
            0, G_C, 0, G_N,
            // 220
            0, G_E, 0, G_I,
            0, G_S, 0, G_A,
            // 230
            0, G_R, 0, G_M,
            0, G_D, 0, G_B,
            // 240
            0, G_H, 0, G_Z,
            0, G_F, 0, G_V,
            // 250
            0, G_W, 0, G_G,
            0, G_X, 0, G_J,
            // 260
            0, G_Q, 0, G_Y,
            0, G_2, 0, G_3,
            // 270
            0, G_1, 0, G_0,
            0, G_7, 0, G_6,
            // 300
            0, G_5, 0, G_4,
            0, G_9, 0, G_8,
            // 310
            0, 0, 0, 0, 0, 0, 0, 0,
            // 320
            0, 0, 0, 0, 0, 0, 0, 0,
            // 330
            0, 0, 0, 0, 0, 0, 0, 0,
            // 340
            0, 0, 0, 0, 0, 0, 0, 0,
            // 350
            0, 0, 0, 0, 0, 0, 0, 0,
            // 360
            0, 0, 0, 0, 0, 0, 0, 0,
            // 370
            0, 0, 0, 0, 0, 0, 0, 0,
        };

        // E64 state
        private byte[] _e64Line = new byte[E64_LINE_WIDTH];
        private int _e64Position;
        private bool _e64LineDirty;
        private bool _e64Overprint;
        private int _e64SkipLines = 1;
        private int _e64LineCount;

        private long MemRead(int addr)
        {
            addr &= 0x7FFF;
            if (addr == 0) return 0;
            return _machine.Memory.Read(addr).Value;
        }

        /// <summary>
        /// E64 полный протокол. aex = M[14] = адрес управления.
        /// </summary>
        private void E64Full(int aex)
        {
            var cpu = _machine.Cpu;

            if (aex == 0) return;
            if (aex == 1) return;

            int ctlAddr = aex;

            // Read E64_Pointer
            long ptrWord = MemRead(ctlAddr);
            int startReg = (int)((ptrWord >> 44) & 0xF);
            int startAddr = (int)((ptrWord >> 29) & 0x7FFF);
            int endReg = (int)((ptrWord >> 25) & 0xF);
            int endAddr = (int)((ptrWord >> 10) & 0x7FFF);
            int flags = (int)(ptrWord & 0x3FF);

            startAddr = (startAddr + (int)cpu.GetM(startReg)) & 0x7FFF;
            endAddr = (endAddr + (int)cpu.GetM(endReg)) & 0x7FFF;

            if (startAddr == 0)
                throw new ProcessorException("E64: bad start_addr");

            if (endAddr <= startAddr)
                endAddr = 0;

            // Initialize line buffer
            Array.Fill(_e64Line, G_SPACE);
            _e64Position = 0;
            _e64LineDirty = false;
            _e64Overprint = false;
            _e64SkipLines = 1;

            // Execute every format word in order (direct port of C++ for(;;) + goto again)
            for (;;)
            {
                ctlAddr++;
again:
                long infoWord = MemRead(ctlAddr);
                int format = (int)((infoWord >> 44) & 0xF);
                int offset = (int)((infoWord >> 37) & 0x7F);
                int digits = (int)((infoWord >> 25) & 0x7F);
                int finish = (int)((infoWord >> 24) & 1);
                int skip = (int)((infoWord >> 21) & 0x7);
                int width = (int)((infoWord >> 13) & 0x7F);
                int repeat = (int)((infoWord >> 1) & 0x7F);

                _e64Position = offset;

                switch (format)
                {
                    case 0: case 8:
                        startAddr = E64PrintGost(startAddr, endAddr);
                        break;
                    case 1: case 5: case 9: case 13:
                        startAddr = E64PrintInstructions(startAddr, endAddr, width, repeat);
                        break;
                    case 2: case 10:
                        startAddr = E64PrintOctal(startAddr, endAddr, digits, width, repeat);
                        break;
                    case 3: case 11:
                        startAddr = E64PrintReal(startAddr, endAddr, digits, width, repeat);
                        break;
                    case 4: case 12:
                        startAddr = E64PrintItm(startAddr, endAddr);
                        break;
                    case 6: case 7: case 14: case 15:
                        startAddr = E64PrintHex(startAddr, endAddr, digits, width, repeat);
                        break;
                }

                if (finish != 0)
                {
                    if (endAddr != 0 && startAddr <= endAddr)
                    {
                        // Repeat printing task until all data expired (C++ "goto again")
                        goto again;
                    }

                    if (_e64Position != 0)
                    {
                        if (!_e64Overprint && (_e64LineDirty || skip > 0))
                            E64EmitLine();
                        else
                        {
                            _e64LineDirty = true;
                            _e64Overprint = false;
                        }
                    }

                    if (skip > 0)
                        _e64SkipLines = skip + 1;
                    break;
                }

                // Check the limit of data pointer
                if (endAddr != 0 && startAddr > endAddr)
                {
                    E64EmitLine();
                    break;
                }
            }

            E64Finish();
        }

        // --- Line buffer ---

        private void E64PutChar(int ch)
        {
            if (_e64Position >= E64_LINE_WIDTH)
            {
                E64EmitLine();
            }
            if (ch != G_SPACE)
            {
                if (_e64LineDirty && !_e64Overprint)
                {
                    int save = _e64Position;
                    E64EmitLine();
                    _e64Position = save;
                }
                if (_e64Overprint && _e64Line[_e64Position] != G_SPACE)
                {
                    E64FlushLine();
                    _output("\\");
                }
                _e64Line[_e64Position] = (byte)ch;
            }
            _e64Position++;
        }

        private void E64EmitLine()
        {
            E64FlushLine();
            _e64Position = 0;
            _e64Overprint = false;
            _e64LineDirty = false;
        }

        private void E64FlushLine()
        {
            if (_e64SkipLines < 0)
            {
                if (_e64LineCount > 0) _output("\n");
                _e64LineCount++;
            }
            else
            {
                for (int i = 0; i < _e64SkipLines; i++)
                {
                    if (_e64LineCount > 0 || i > 0) _output("\n");
                    _e64LineCount++;
                }
            }
            _e64SkipLines = 1;

            int limit = _e64Line.Length;
            while (limit > 0 && _e64Line[limit - 1] == G_SPACE)
                limit--;

            if (limit > 0)
            {
                var sb = new StringBuilder(limit);
                for (int i = 0; i < limit; i++)
                {
                    sb.Append(CosyCodec.GostToUnicode(_e64Line[i]));
                }
                _output(sb.ToString());
            }

            Array.Fill(_e64Line, G_SPACE);
        }

        private void E64Finish()
        {
            if (_e64LineDirty)
                E64EmitLine();
            if (_e64LineCount > 0)
            {
                _output("\n");
                _e64LineCount = 0;
            }
        }

        // --- GOST text ---

        private int E64PrintGost(int startAddr, int endAddr)
        {
            int wordAddr = startAddr;
            int byteIdx = 0;
            byte lastCh = G_SPACE;

            while (wordAddr != 0)
            {
                if (endAddr != 0 && wordAddr == endAddr + 1)
                    return wordAddr;

                long word = MemRead(wordAddr);
                byte ch = (byte)((word >> (40 - byteIdx * 8)) & 0xFF);

                if (IsGostEndOfText(ch))
                {
                    if (byteIdx != 0) wordAddr++;
                    return wordAddr;
                }

                byteIdx++;
                if (byteIdx >= 6) { wordAddr++; byteIdx = 0; }

                if (_e64Position == E64_LINE_WIDTH)
                {
                    E64EmitLine();
                    continue;
                }

                switch (ch)
                {
                    case G_NEWPAGE:
                        if (_e64LineDirty) E64EmitLine();
                        E64PutChar(G_SPACE);
                        _e64SkipLines = -1;
                        break;

                    case G_CR:
                    case G_NEWLINE:
                        if (_e64LineDirty && !_e64Overprint)
                            E64EmitLine();
                        E64EmitLine();
                        break;

                    case G_NULL_WIDTH:
                    case G_NULL_WIDTH2:
                        break;

                    case G_SET_POS:
                    case G_SET_POS2:
                    {
                        long word2 = MemRead(wordAddr);
                        int nextIdx = byteIdx + 1;
                        if (nextIdx >= 6) nextIdx = 0;
                        byte pos = (byte)((word2 >> (40 - nextIdx * 8)) & 0xFF);
                        _e64Position = pos % E64_LINE_WIDTH;
                        byteIdx++;
                        if (byteIdx >= 6) { wordAddr++; byteIdx = 0; }
                        break;
                    }

                    case G_EOLN:
                    case G_REPEAT:
                    {
                        long word2 = MemRead(wordAddr);
                        int nextIdx = byteIdx + 1;
                        if (nextIdx >= 6) nextIdx = 0;
                        byte count = (byte)((word2 >> (40 - nextIdx * 8)) & 0xFF);
                        byteIdx += 2;
                        if (byteIdx >= 6) { wordAddr++; byteIdx -= 6; }

                        while (count-- > 0)
                        {
                            if (_e64Position == E64_LINE_WIDTH)
                                E64EmitLine();
                            if (_e64Line[_e64Position] == G_SPACE)
                                E64PutChar(lastCh);
                            else
                                _e64Position++;
                        }
                        break;
                    }

                    case G_OVERPRINT:
                        _e64Overprint = true;
                        ch = G_SPACE;
                        goto default;

                    case G_SPACE:
                    case G_SPACE2:
                    case G_SPACE3:
                        ch = G_SPACE;
                        goto default;

                    default:
                        lastCh = ch;
                        E64PutChar(ch);
                        break;
                }
            }
            return 0;
        }

        private static bool IsGostEndOfText(byte ch)
            => ch == G_EOF || ch == G_END_OF_INFO || ch == 0x99; // 0231

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
                    value = Besm6Math.Besm6ToDouble(word);
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

        // --- Instructions ---

        private int E64PrintInstructions(int startAddr, int endAddr,
            int width, int repeat)
        {
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

                int a = (int)(word >> 24) & 0xFFFFFF;
                int b = (int)(word & 0xFFFFFF);

                E64PrintCmd(a);
                E64PutChar(G_SPACE);
                E64PrintCmd(b);

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
                if (width > 23)
                    _e64Position += width - 23;
            }
            return 0;
        }

        private void E64PrintCmd(int cmd)
        {
            E64PutChar((cmd >> 23) & 1);
            E64PutChar((cmd >> 20) & 7);
            E64PutChar(G_SPACE);
            if ((cmd & 0x200000) != 0) // 02000000
            {
                E64PutChar((cmd >> 18) & 3);
                E64PutChar((cmd >> 15) & 7);
                E64PutChar(G_SPACE);
                E64PutChar((cmd >> 12) & 7);
            }
            else
            {
                E64PutChar((cmd >> 18) & 1);
                E64PutChar((cmd >> 15) & 7);
                E64PutChar((cmd >> 12) & 7);
                E64PutChar(G_SPACE);
            }
            E64PutChar((cmd >> 9) & 7);
            E64PutChar((cmd >> 6) & 7);
            E64PutChar((cmd >> 3) & 7);
            E64PutChar(cmd & 7);
        }
    }
}