using Besm6.Processor.Cpu;
using Besm6.Processor.Memory;
using Besm6.Runtime.Encoding;
using Besm6.Runtime.Storage;

namespace Besm6.Runtime.Extracodes
{
    internal sealed partial class HostedStorageService
    {
        // ─── E70: disk/drum I/O ──────────────────────────────────────────────
        internal void E70()
        {
            var cpu = _cpu;
            long execAddr = cpu.GetM(M16) & 0x7FFF;
            long ctrl = (execAddr == 0) ? (long)cpu.A.Value : (long)_memory.Read((uint)execAddr).Value;
            bool isRead = (ctrl & (1L << 39)) != 0;
            int unit = (int)((ctrl >> 12) & 0x3F);
            int page = (int)((ctrl >> 30) & 0x1F);
            // Детальная диагностика E70 (BESM6_TRACE): декодирование слова управления.
            if (_trace.Enabled)
            {
                int zoneF = (int)(ctrl & 0xFFF);
                int seek = (int)((ctrl >> 40) & 1);
                int tract = (int)(ctrl & 0x1F);
                int sector = (int)((ctrl >> 6) & 0x3);
                int paragraph = (int)((ctrl >> 24) & 0x3);
                int rawSect = (int)((ctrl >> 35) & 1);
                int physIo = (int)((ctrl >> 38) & 1);
                int sectIo = (int)((ctrl >> 47) & 1);
                string medium;
                if (unit >= 24 && unit < 56)
                {
                    TapeImage? d = _tapes.DiskByUnit(unit);
                    medium = d == null ? "DISK(!!not-mounted!!)" : $"DISK(tape=0{d.VolumeId:X})";
                }
                else
                {
                    int thisDrum = unit & 31;
                    medium = physIo == 1 ? $"PHYSIO(drum=0{thisDrum:X},mapped=0{_mappedDrum:X})" : $"DRUM(0{thisDrum:X})";
                }

                _trace.WriteLine($"[E70] m16=0{Convert.ToString(execAddr, 8)} cw=0{ctrl:X12} op={(isRead ? "R" : "W")}{(seek == 1 ? "(seek)" : "")} " + $"unit=0{Convert.ToString(unit, 8)} page=0{Convert.ToString(page, 8)} zone=0{Convert.ToString(zoneF, 8)} " + $"tract=0{Convert.ToString(tract, 8)} sect={sector} par=0{Convert.ToString(paragraph, 8)} rawSect={rawSect} " + $"physIo={physIo} sectIo={sectIo} -> {medium}");
            }

            if (unit >= 24 && unit < 56)
            {
                if ((ctrl & (1L << 40)) != 0)
                    return;
                int zone = (int)(ctrl & 0xFFF);
                int memAddr = page << 10;
                TapeImage? disk = _tapes.DiskByUnit(unit);
                if (disk == null)
                {
                    // MONSYS при загрузке может обращаться к дискам, которые ещё
                    // не были смонтированы через E57 ASSIGN.
                    _tapes.MountTape(0, unit);
                    disk = _tapes.DiskByUnit(unit);
                    if (disk == null)
                        throw new ProcessorException($"E70: disk unit 0{Convert.ToString(unit, 8)} not mounted");
                }

                if (isRead)
                    disk.ReadToMemory(_memory, (uint)zone, 0, memAddr, 1024);
                else
                    disk.WriteFromMemory(_memory, (uint)zone, 0, memAddr, 1024);
            }
            else
            {
                int tract = (int)(ctrl & 0x1F);
                int sector = (int)((ctrl >> 6) & 0x3);
                int paragraph = (int)((ctrl >> 24) & 0x3);
                bool physIo = (ctrl & (1L << 38)) != 0;
                bool sectIo = (ctrl & (1L << 47)) != 0;
                bool rawSect = (ctrl & (1L << 35)) != 0;
                int memAddr = page << 10;
                if (sectIo)
                    memAddr += paragraph << 8;
                if (rawSect && sectIo)
                {
                    int raw = (int)(ctrl & 0xFFF);
                    sector = raw & 3;
                    tract = (raw >> 2) & 31;
                }

                int thisDrum = unit & 31;
                if (physIo && _mappedDrum >= 0 && thisDrum >= _mappedDrum)
                {
                    if (_physIoDisk != null)
                    {
                        int diskZone = tract + (thisDrum - _mappedDrum) * 32;
                        if (!sectIo)
                        {
                            if (isRead)
                                _physIoDisk.ReadToMemory(_memory, (uint)diskZone, 0, memAddr, 1024);
                            else
                                _physIoDisk.WriteFromMemory(_memory, (uint)diskZone, 0, memAddr, 1024);
                        }
                        else
                        {
                            if (isRead)
                                _physIoDisk.ReadToMemory(_memory, (uint)diskZone, (uint)sector, memAddr, 256);
                            else
                                _physIoDisk.WriteFromMemory(_memory, (uint)diskZone, (uint)sector, memAddr, 256);
                        }
                    }

                    return;
                }

                int nwords = sectIo ? 256 : 1024;
                TapeImage? drum = _tapes.DrumByUnit(thisDrum);
                if (drum == null)
                    throw new ProcessorException($"E70: drum unit 0{Convert.ToString(thisDrum, 8)} not available");
                if (isRead)
                    drum.ReadToMemory(_memory, (uint)tract, sectIo ? (uint)sector : 0, memAddr, nwords);
                else
                    drum.WriteFromMemory(_memory, (uint)tract, sectIo ? (uint)sector : 0, memAddr, nwords);
            }
        }
    }
}
