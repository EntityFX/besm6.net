using System;
using System.Text;

namespace Besm6.Runtime
{
    /// <summary>
    /// E64 GOST text formatter.
    /// </summary>
    public sealed partial class ExtracodeHandler
    {

        private int E64PrintGost(int startAddr, int endAddr)
        {
            var bp = new BytePointer(_machine.Memory, (uint)startAddr);
            byte lastCh = G_SPACE;

            for (;;)
            {
                if (bp.WordAddr == 0)
                    return 0;

                if (endAddr != 0 && bp.WordAddr == endAddr + 1)
                    return (int)bp.WordAddr;

                byte ch = bp.Get();
                if (IsGostEndOfText(ch))
                {
                    if (bp.ByteIndex != 0)
                        bp.WordAddr++;
                    return (int)bp.WordAddr;
                }

                // Weirdness of e64 in Dispak: when '231' or other EOF
                // is present in the current word - byte #129 is ignored.
                if (_e64Position == E64_LINE_WIDTH)
                {
                    E64EmitLine();
                    if (EofInWord((int)bp.WordAddr))
                        continue;
                }

                // Weirdness of e64 in Dispak: overprint '212'
                // is valid only at position #2, but it affects
                // previous character as well.
                if (_e64Position == 0 && bp.Peek() == G_OVERPRINT)
                    _e64Overprint = true;

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
                        ch = bp.Get();
                        _e64Position = ch % E64_LINE_WIDTH;
                        break;

                    case G_EOLN:
                    case G_REPEAT:
                        ch = bp.Get();
                        while (ch-- > 0)
                        {
                            if (_e64Position == E64_LINE_WIDTH)
                                E64EmitLine();
                            if (_e64Line[_e64Position] == G_SPACE)
                                E64PutChar(lastCh);
                            else
                                _e64Position += 1;
                        }
                        break;

                    case G_OVERPRINT:
                        _e64Overprint = true;
                        goto case G_SPACE;

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
        }

        private bool EofInWord(int wordAddr)
        {
            long w = MemRead(wordAddr);
            for (int i = 0; i < 6; i++)
            {
                byte b = (byte)((w >> (40 - i * 8)) & 0xFF);
                if (b == 0x99 || b == G_EOF || b == G_END_OF_INFO) // 0231, 0377, 0172
                    return true;
            }
            return false;
        }

        private static bool IsGostEndOfText(byte ch)
            => ch == G_EOF || ch == G_END_OF_INFO || ch == 0x99; // 0231
    }
}
