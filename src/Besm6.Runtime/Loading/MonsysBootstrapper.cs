using Besm6.Assembler;
#pragma warning disable CS0618

namespace Besm6.Runtime
{
    /// <summary>
    /// MONSYS bootstrap: подготовка tape, запись drum, установка начального K.
    /// </summary>
    internal sealed class MonsysBootstrapper
    {
        private readonly MachineCore _machine;
        private readonly TapeMountService _tapes;
        private readonly ExtracodeHandler _extracode;
        private readonly Action<string>? _verboseLog;

        public MonsysBootstrapper(MachineCore machine, TapeMountService tapes,
            ExtracodeHandler extracode, Action<string>? verboseLog)
        {
            _machine = machine;
            _tapes = tapes;
            _extracode = extracode;
            _verboseLog = verboseLog;
        }

        /// <summary>
        /// Подготовить загрузчик MONSYS: данные таблиц 03000-03010 и стартовый код.
        /// Точный порт Machine::boot_ms_dubna из dubna/machine.cpp (930-975).
        /// </summary>
        public void Boot()
        {
            var mem = _machine.Memory;
            var asm = new AutoDetectingAssembler().AssembleWord;

            _tapes.MountTape(24, TapeImage.TapeMonsys);
            if (_tapes.GetDiskByUnit(24) is { } monsysDisk)
                _extracode.MapDrumToDisk(17, 24, monsysDisk);

            mem.Write(1032, new Word48((ulong)asm("vtm -5(1),     *70 3002")));
            mem.Write(1033, new Word48((ulong)asm("xta 377,       atx 3010")));
            mem.Write(1034, new Word48((ulong)asm("xta 363,       atx 100")));
            mem.Write(1035, new Word48((ulong)asm("vtm 53401(17), utc")));
            mem.Write(1036, new Word48((ulong)asm("*70 3010(1),   utc")));
            mem.Write(1037, new Word48((ulong)asm("vlm 2014(1),   ita 17")));
            mem.Write(1038, new Word48((ulong)asm("atx 716,       *70 717")));
            mem.Write(1039, new Word48((ulong)asm("xta 17,        ati 16")));
            mem.Write(1040, new Word48((ulong)asm("atx 2(16),     arx 3001")));
            mem.Write(1041, new Word48((ulong)asm("atx 17,        xta 3000")));
            mem.Write(1042, new Word48((ulong)asm("atx (16),      vtm 1673(15)")));
            mem.Write(1043, new Word48((ulong)asm("uj (17),       utc")));

            mem.Write(1536, new Word48(183533445462124L));
            mem.Write(1537, new Word48(8L));
            mem.Write(1538, new Word48(141562122145921L));
            mem.Write(1539, new Word48(65536L));
            mem.Write(1540, new Word48(824633790471L));
            mem.Write(1541, new Word48(69632L));
            mem.Write(1542, new Word48(824633790472L));
            mem.Write(1543, new Word48(69633L));
            mem.Write(1544, new Word48(824633790493L));

            _machine.Cpu.SetK(1032);
        }
    }
}