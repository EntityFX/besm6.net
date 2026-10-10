namespace Besm6.Architecture
{
    /// <summary>
    /// Кодирует и декодирует команды БЭСМ-6 без зависимости от процессора или ассемблера.
    /// </summary>
    public static class InstructionCodec
    {
        private const uint HalfMask = 0xFFFFFF;
        private const ulong WordMask = 0xFFFFFFFFFFFF;

        /// <summary>Декодирует одно 24-битное полуслово команды.</summary>
        public static DecodedInstruction DecodeHalf(uint halfWord)
        {
            halfWord &= HalfMask;
            byte register = (byte)((halfWord >> 20) & 0xF);
            bool isLong = (halfWord & (1u << 19)) != 0;
            Opcode opcode = isLong
                ? (Opcode)((halfWord >> 12) & 0xF8)
                : (Opcode)((halfWord >> 12) & 0x3F);
            ushort address = (ushort)(halfWord & (isLong ? 0x7FFFu : 0xFFFu));
            if (!isLong && (halfWord & (1u << 18)) != 0)
                address |= 0x7000;

            return new DecodedInstruction(
                register,
                opcode,
                address,
                isLong ? InstructionFormat.Long : InstructionFormat.Short);
        }

        /// <summary>Кодирует команду в одно 24-битное полуслово.</summary>
        public static uint EncodeHalf(DecodedInstruction instruction)
        {
            if (instruction.Register >= ArchitectureConstants.IndexRegCount)
                throw new ArgumentOutOfRangeException(nameof(instruction), "Номер регистра должен быть в диапазоне 0–15.");
            if (instruction.Address > ArchitectureConstants.AddrMask)
                throw new ArgumentOutOfRangeException(nameof(instruction), "Адрес должен быть 15-битным.");

            uint register = (uint)instruction.Register << 20;
            uint opcode = (uint)instruction.Opcode;
            uint address = instruction.Address;

            if (instruction.Format == InstructionFormat.Long)
            {
                if (opcode is < 0x80 or > 0xF8 || (opcode & 0x7) != 0)
                    throw new ArgumentOutOfRangeException(nameof(instruction), "Длинный код операции должен быть в диапазоне 0200–0370 с шагом 010.");

                return (register | (opcode << 12) | address) & HalfMask;
            }

            if (opcode > 0x3F)
                throw new ArgumentOutOfRangeException(nameof(instruction), "Короткий код операции должен быть в диапазоне 000–077.");
            if (address > 0xFFF && address < 0x7000)
                throw new ArgumentOutOfRangeException(nameof(instruction), "Короткая команда не представляет адреса 010000–067777.");

            uint extension = address >= 0x7000 ? 1u << 18 : 0;
            return (register | extension | (opcode << 12) | (address & 0xFFF)) & HalfMask;
        }

        /// <summary>Декодирует левое и правое полуслово 48-битного слова.</summary>
        public static (DecodedInstruction Left, DecodedInstruction Right) DecodeWord(ulong word)
        {
            word &= WordMask;
            return (
                DecodeHalf((uint)(word >> 24)),
                DecodeHalf((uint)(word & HalfMask)));
        }

        /// <summary>Кодирует два полусловия в 48-битное слово.</summary>
        public static ulong EncodeWord(DecodedInstruction left, DecodedInstruction right)
        {
            return (((ulong)EncodeHalf(left) << 24) | EncodeHalf(right)) & WordMask;
        }
    }
}
