using System;
using System.Text;

namespace Besm6.Runtime
{
    /// <summary>
    /// E64 Dubna raw byte formatter.
    /// </summary>
    public sealed partial class ExtracodeHandler
    {

        // --- Dubna mode (flags & 010 oct): raw byte stream ---

        private void E64PrintDubna(int startAddr, int endAddr)
        {
            var bp = new BytePointer(_machine.Memory, (uint)startAddr);

            byte ch = bp.Get();
            if (ch > 0 && _e64LineDirty)
            {
                // Emit previous line.
                E64EmitLine();
            }
            for (;;)
            {
                if (endAddr != 0 && bp.WordAddr == endAddr + 1)
                    return;

                if (_e64Position == E64_LINE_WIDTH)
                    return;

                ch = bp.Get();
                if (ch == 0x7E) // 0176 — end of text
                {
                    E64EmitLine();
                    return;
                }

                if (ch >= 0x80) // 0200 — packed spaces
                {
                    while (ch-- >= 0x80 && _e64Position < E64_LINE_WIDTH)
                        E64PutChar(G_SPACE);
                }
                else if (ch < 0x60) // 0140 — печатный символ ГОСТ
                {
                    E64PutChar(ch);
                }
            }
        }
    }
}
