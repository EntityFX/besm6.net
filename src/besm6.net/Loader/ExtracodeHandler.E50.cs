using System;
using System.Globalization;
using Besm6.Core;

namespace Besm6.Loader
{
    public sealed partial class ExtracodeHandler
    {
        private int _e50ParseIndex;
        private int _e50ParseLastWordAddr;
        private int _e50ParseLastByteIndex;

        private static bool IsDigit(byte c) => c >= (byte)'0' && c <= (byte)'9';

        private static bool IsChar(byte c)
            => (c >= 65 && c <= 90) || (c >= 96 && c <= 126); // A-Z or ` to ~

        /// <summary>
        /// E50 014: распознаватель текстовой строки (порт dubna/e50.cpp e50_parse).
        /// Вывод: ACC = значение (число/идент), M[14] = тип (0..6), RMR = детали.
        /// </summary>
        private void E50Parse()
        {
            var cpu = _machine.Cpu;
            long input = (long)cpu.GetAcc().Value;

            int srcAddr = (int)(input & 0x7FFF);
            bool starSlashFlag = ((input >> 16) & 1) != 0;
            bool charMode = ((input >> 18) & 1) != 0;
            int srcReg = (int)((input >> 20) & 0xF);

            var mem = _machine.Memory;

            // Resume state.
            int wordAddr = _e50ParseLastWordAddr;
            int byteIdx = _e50ParseLastByteIndex;
            int index = _e50ParseIndex;

            if (srcReg != 0)
                srcAddr = (srcAddr + (int)cpu.GetM(srcReg)) & 0x7FFF;

            if (srcAddr != 0)
            {
                // Set source pointer.
                wordAddr = srcAddr;
                byteIdx = 0;
                index = 0;
            }
            else
            {
                // Continue from current place.
                if (wordAddr == 0)
                {
                    cpu.SetM(14, 0); // parse error
                    cpu.SetAcc(0);
                    return;
                }
            }

            // ─── Char mode (positional scanning) ───
            if (charMode)
            {
                var bp = new BytePointer(mem, (uint)wordAddr, (uint)byteIdx);
                for (; ; )
                {
                    if (bp.WordAddr == 0)
                    {
                        cpu.SetM(14, 0);
                        cpu.SetRmr((ulong)index << 24);
                        cpu.SetAcc(0);
                        return;
                    }
                    byte c = bp.Get();
                    index++;
                    if (c == 0 || c == 0x0A)
                    {
                        cpu.SetM(14, 0);
                        cpu.SetRmr(((ulong)index << 24) | c);
                        long result = ((long)c << 40) | ((long)' ' << 32) | ((long)' ' << 24)
                                    | ((long)' ' << 16) | ((long)' ' << 8) | ' ';
                        cpu.SetAcc((ulong)result);
                    }
                    else if (IsDigit(c))
                    {
                        cpu.SetM(14, 1);
                        cpu.SetRmr(((ulong)index << 24) | c);
                        long result = ((long)c << 40) | ((long)' ' << 32) | ((long)' ' << 24)
                                    | ((long)' ' << 16) | ((long)' ' << 8) | ' ';
                        cpu.SetAcc((ulong)result);
                    }
                    else if ((c == '*' || c == '/') && starSlashFlag)
                    {
                        cpu.SetM(14, 2);
                        cpu.SetRmr(((ulong)index << 24) | c);
                        long result = ((long)c << 40) | ((long)' ' << 32) | ((long)' ' << 24)
                                    | ((long)' ' << 16) | ((long)' ' << 8) | ' ';
                        cpu.SetAcc((ulong)result);
                    }
                    else if (IsChar(c))
                    {
                        cpu.SetM(14, 2);
                        cpu.SetRmr(((ulong)index << 24) | c);
                        long result = ((long)c << 40) | ((long)' ' << 32) | ((long)' ' << 24)
                                    | ((long)' ' << 16) | ((long)' ' << 8) | ' ';
                        cpu.SetAcc((ulong)result);
                    }
                    else
                    {
                        cpu.SetM(14, 3);
                        cpu.SetRmr(((ulong)index << 24) | c);
                        long result = ((long)c << 40) | ((long)' ' << 32) | ((long)' ' << 24)
                                    | ((long)' ' << 16) | ((long)' ' << 8) | ' ';
                        cpu.SetAcc((ulong)result);
                    }
                    // Save state and return.
                    _e50ParseLastWordAddr = (int)bp.WordAddr;
                    _e50ParseLastByteIndex = (int)bp.ByteIndex;
                    _e50ParseIndex = index;
                    return;
                }
            }

            // ─── Token mode ───
            {
                var bp = new BytePointer(mem, (uint)wordAddr, (uint)byteIdx);
                for (; ; )
                {
                    if (bp.WordAddr == 0)
                    {
                        cpu.SetM(14, 0);
                        cpu.SetAcc(0);
                        return;
                    }
                    byte c = bp.Get();
                    index++;
                    switch (c)
                    {
                        case 0:
                        case 0x0A:
                            cpu.SetM(14, 6);
                            _e50ParseLastWordAddr = (int)bp.WordAddr;
                            _e50ParseLastByteIndex = (int)bp.ByteIndex;
                            _e50ParseIndex = index;
                            cpu.SetAcc(c);
                            return;

                        case (byte)' ':
                            continue;

                        case (byte)'0': case (byte)'1': case (byte)'2':
                        case (byte)'3': case (byte)'4': case (byte)'5':
                        case (byte)'6': case (byte)'7': case (byte)'8':
                        case (byte)'9':
                            {
                                long value = c - '0';
                                for (; ; )
                                {
                                    c = bp.Get();
                                    index++;
                                    if (!IsDigit(c)) break;
                                    value = (value << 3) + (c - '0');
                                }
                                cpu.SetM(14, 1);
                                cpu.SetRmr(((ulong)index << 24) | c);
                                _e50ParseLastWordAddr = (int)bp.WordAddr;
                                _e50ParseLastByteIndex = (int)bp.ByteIndex;
                                _e50ParseIndex = index;
                                cpu.SetAcc((ulong)value);
                                return;
                            }

                        case (byte)'-':
                            {
                                byte peek = bp.Peek();
                                if (IsDigit(peek))
                                {
                                    c = bp.Get();
                                    index++;
                                    long value = c - '0';
                                    for (; ; )
                                    {
                                        c = bp.Get();
                                        index++;
                                        if (!IsDigit(c)) break;
                                        value = (value << 3) + (c - '0');
                                    }
                                    cpu.SetM(14, 1);
                                    cpu.SetRmr(((ulong)index << 24) | c);
                                    _e50ParseLastWordAddr = (int)bp.WordAddr;
                                    _e50ParseLastByteIndex = (int)bp.ByteIndex;
                                    _e50ParseIndex = index;
                                    cpu.SetAcc((ulong)(-value));
                                    return;
                                }
                                cpu.SetM(14, 6);
                                _e50ParseLastWordAddr = (int)bp.WordAddr;
                                _e50ParseLastByteIndex = (int)bp.ByteIndex;
                                _e50ParseIndex = index;
                                cpu.SetAcc((byte)'-');
                                return;
                            }

                        case (byte)'*':
                        case (byte)'/':
                            if (starSlashFlag)
                            {
                                E50ParseIdent(ref bp, ref index, starSlashFlag, c);
                                return;
                            }
                            cpu.SetM(14, 6);
                            _e50ParseLastWordAddr = (int)bp.WordAddr;
                            _e50ParseLastByteIndex = (int)bp.ByteIndex;
                            _e50ParseIndex = index;
                            cpu.SetAcc(c);
                            return;

                        default:
                            if (!IsChar(c))
                            {
                                cpu.SetM(14, 6);
                                _e50ParseLastWordAddr = (int)bp.WordAddr;
                                _e50ParseLastByteIndex = (int)bp.ByteIndex;
                                _e50ParseIndex = index;
                                cpu.SetAcc(c);
                                return;
                            }
                            E50ParseIdent(ref bp, ref index, starSlashFlag, c);
                            return;
                    }
                }
            }
        }

        private void E50ParseIdent(ref BytePointer bp, ref int index, bool starSlashFlag, byte firstChar)
        {
            var cpu = _machine.Cpu;
            char[] ident = new char[16];
            int identLen = 0;
            ident[identLen++] = (char)firstChar;
            while (identLen < 16)
            {
                byte c = bp.Get();
                index++;
                if (!IsChar(c) && !IsDigit(c) && !(starSlashFlag && (c == '*' || c == '/')))
                    break;
                ident[identLen++] = (char)c;
            }
            for (int i = identLen; i < 16; i++) ident[i] = ' ';

            cpu.SetM(14, 4);
            long rmr = 0;
            for (int i = 6; i < 12; i++)
                rmr |= (long)(byte)ident[i] << ((11 - i) * 8);
            cpu.SetRmr((ulong)rmr);
            _e50ParseLastWordAddr = (int)bp.WordAddr;
            _e50ParseLastByteIndex = (int)bp.ByteIndex;
            _e50ParseIndex = index;
            long acc = 0;
            for (int i = 0; i < 6; i++)
                acc |= (long)(byte)ident[i] << ((5 - i) * 8);
            cpu.SetAcc((ulong)acc);
        }

        // ─── E50 017: format real number (порт dubna/e50.cpp e50_format_real) ──

        private void E50Format()
        {
            var cpu = _machine.Cpu;
            long input = (long)cpu.GetAcc().Value;

            int destAddr = (int)(input & 0x7FFF);
            bool rightAlign = ((input >> 15) & 1) != 0;
            int destReg = (int)((input >> 16) & 0xF);
            int srcAddr = (int)((input >> 20) & 0x7FFF);
            int width = (int)((input >> 35) & 0x1F);
            int precision = (int)((input >> 40) & 0xF);
            int srcReg = (int)((input >> 44) & 0xF);

            int actualSrc = srcAddr;
            if (srcReg != 0)
                actualSrc = (srcAddr + (int)cpu.GetM(srcReg)) & 0x7FFF;
            double value = _machine.Memory.Read((uint)actualSrc).ToDouble();

            int outAddr = destAddr;
            if (destAddr != 0 || destReg != 0)
                outAddr = (destAddr + (int)cpu.GetM(destReg)) & 0x7FFF;

            if (width == 0)
            {
                cpu.SetM(14, 0);
                cpu.SetAcc(0);
                return;
            }

            string result;
            string scientific = value.ToString("E" + precision, CultureInfo.InvariantCulture).ToUpperInvariant();

            if (GoodForFixedFormat(value, precision))
            {
                string fixedPoint = value.ToString("F" + precision, CultureInfo.InvariantCulture);
                if (fixedPoint.Length <= scientific.Length)
                    result = fixedPoint;
                else
                    result = scientific;
            }
            else
            {
                result = scientific;
            }

            bool overflow = result.Length > width;

            if (!rightAlign)
            {
                if (result.Length < width)
                    result = result.PadRight(width);
                else if (overflow)
                    result = result.Substring(0, width);
            }
            else if (overflow)
            {
                result = result.Substring(result.Length - width);
            }
            else if (result.Length < width)
            {
                result = result.PadLeft(width);
            }

            var mem = _machine.Memory;
            var bp = new BytePointer(mem, (uint)outAddr, 0);
            foreach (char ch in result)
                bp.Put((byte)ch);
            while (bp.ByteIndex != 0)
                bp.Put((byte)' ');

            cpu.SetM(14, (uint)(overflow ? 1 : 0));
            cpu.SetAcc((ulong)width);
        }

        private static bool GoodForFixedFormat(double value, int precision)
        {
            if (value < 0) value = -value;
            if (value == 0) return true;
            if (value >= 1) return true;
            value *= Math.Pow(10, precision);
            return value >= 1;
        }
    }
}