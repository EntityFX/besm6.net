using System;
using System.Text;

namespace Besm6.Runtime
{
    /// <summary>
    /// E64 instruction formatter.
    /// </summary>
    public sealed partial class ExtracodeHandler
    {

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
