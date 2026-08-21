namespace Besm6.Core
{
    /// <summary>
    /// Коды экстракодов БЭСМ-6 (восьмеричные номера, как в оригинале).
    /// В Processore передаются как decimal (050 oct = 40 dec и т.д.).
    /// </summary>
    public enum Extracode
    {
        E50 = 40, // 050 oct — математика / parse
        E51 = 41, // 051 oct — sin
        E52 = 42, // 052 oct — cos
        E53 = 43, // 053 oct — atan
        E54 = 44, // 054 oct — asin
        E55 = 45, // 055 oct — log
        E56 = 46, // 056 oct — exp
        E57 = 47, // 057 oct — монтаж лент
        E60 = 48, // 060 oct — (reserved)
        E61 = 49, // 061 oct — (reserved)
        E62 = 50, // 062 oct — (reserved)
        E63 = 51, // 063 oct — ОС Дубна
        E64 = 52, // 064 oct — вывод текста
        E65 = 53, // 065 oct — выключатели
        E66 = 54, // 066 oct — (reserved)
        E67 = 55, // 067 oct — отладка (jump)
        E70 = 56, // 070 oct — диск/барабан I/O
        E71 = 57, // 071 oct — терминал / перфокарты
        E72 = 58, // 072 oct — страницы памяти
        E73 = 59, // 073 oct — ITM/ASS
        E74 = 60, // 074 oct — finish job
        E75 = 61, // 075 oct — write ACC to memory
        E76 = 62, // 076 oct — kernel routine
        // Long extracodes (opcode & 0o200)
        E20 = 128, // 200 oct — (reserved/no-op)
        E21 = 136, // 210 oct — lock/release semaphores (no-op)
    }
}
