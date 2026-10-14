namespace Besm6.Architecture.Machine
{
    public interface IRegisterOps : IRegisters
    {
        /// <summary>Устанавливает индексный регистр M с указанным номером.</summary>
        void SetM(int index, uint val);

        /// <summary>Возвращает индексный регистр M с указанным номером.</summary>
        uint GetM(int index);

        /// <summary>Устанавливает регистр режима R.</summary>
        void SetR(ulong val);
        /// <summary>Устанавливает аккумулятор A.</summary>
        void SetA(ulong val);
        /// <summary>Устанавливает регистр младших разрядов Y.</summary>
        void SetY(ulong val);
    }
}
