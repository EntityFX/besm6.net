using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Besm6.Runtime
{
    /// <summary>
    /// Владеет жизненным циклом лент: mount, release, file search/mount, scratch, required tapes.
    /// </summary>
    internal sealed class TapeMountService
    {
        private readonly Dictionary<int, TapeImage> _disksByUnit = new();
        private readonly Dictionary<long, TapeImage> _disksByTapeId = new();
        private readonly HashSet<TapeImage> _fileBackedTapes = new();
        private readonly Dictionary<int, TapeImage> _drumsByUnit = new();
        private readonly List<string> _filePaths = new();
        private readonly string? _tapesDir;
        private readonly Action<string>? _verboseLog;

        public TapeMountService(string? tapesDir, Action<string>? verboseLog = null)
        {
            _tapesDir = tapesDir;
            _verboseLog = verboseLog;
            _drumsByUnit[1] = new TapeImage(0, new byte[TapeImage.DrumNWords * 6], readOnly: false);
        }

        public TapeImage? GetDrumByUnit(int unit)
        {
            if (!_drumsByUnit.TryGetValue(unit, out var d))
            {
                d = new TapeImage(0, new byte[TapeImage.DrumNWords * 6], readOnly: false);
                _drumsByUnit[unit] = d;
            }
            return d;
        }

        public bool MountTape(int unit, long tapeId, bool writePermit = false)
        {
            if (unit < 24 || unit >= 56)
                throw new ProcessorException($"Invalid disk unit {Convert.ToString(unit, 8)} in disk mount");

            if (_disksByUnit.TryGetValue(unit, out TapeImage? mounted))
                return mounted.VolumeId == tapeId;

            var path = TapeImage.FindImagePath(tapeId, _tapesDir);
            if (path == null)
            {
                if (_verboseLog != null) _verboseLog($"Tape image for id 0x{tapeId:X12} not found, creating empty disk");
                var empty = new TapeImage(tapeId, new byte[TapeImage.PageNWords * 6 * 288], readOnly: !writePermit);
                _disksByUnit[unit] = empty;
                _disksByTapeId[tapeId] = empty;
                return true;
            }

            return MountFileBackedTape(unit, tapeId, path, writePermit);
        }

        private bool MountFileBackedTape(int unit, long tapeId, string path, bool writePermit)
        {
            var image = TapeImage.LoadFromFile(tapeId, path, readOnly: !writePermit);
            _disksByUnit[unit] = image;
            _disksByTapeId[tapeId] = image;
            _fileBackedTapes.Add(image);
            if (_verboseLog != null) _verboseLog($"Mounted {path} as disk 0{unit:X}");
            return true;
        }

        public void ReleaseTapes(long mask)
        {
            ulong bitmask = (ulong)mask;
            for (int diskIndex = 0; diskIndex < 32; diskIndex++)
            {
                if (((bitmask >> (47 - diskIndex)) & 1UL) == 0)
                    continue;

                int unit = 24 + diskIndex;
                TapeImage? released = null;
                if (_disksByUnit.Remove(unit, out released) &&
                    _disksByTapeId.TryGetValue(released.VolumeId, out TapeImage? indexed) &&
                    ReferenceEquals(released, indexed))
                {
                    TapeImage? replacement = _disksByUnit.Values.FirstOrDefault(
                        disk => disk.VolumeId == released.VolumeId);
                    if (replacement == null)
                        _disksByTapeId.Remove(released.VolumeId);
                    else
                        _disksByTapeId[released.VolumeId] = replacement;
                }

                if (released != null && !_disksByUnit.Values.Any(
                    disk => ReferenceEquals(disk, released)))
                    _fileBackedTapes.Remove(released);
            }
        }

        public uint FileSearch(ulong discId, ulong fileName, bool writeMode)
        {
            const ulong discLocal = 0xB2F8E1B00000UL;
            const ulong discHome = 0xA2FB65000000UL;
            const ulong discTmp = 0xD2DC00000000UL;
            string? directory = (discId & 0xFFFFFFFFF000UL) switch
            {
                discLocal => Directory.GetCurrentDirectory(),
                discHome => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                discTmp => Path.GetTempPath(),
                _ => null
            };
            if (string.IsNullOrEmpty(directory))
                return 0;

            string filename = IsoFilename(fileName);
            if (filename.Length == 0 || filename != Path.GetFileName(filename))
                return 0;

            string path = Path.Combine(directory, filename + ".bin");
            bool exists = File.Exists(path) || File.Exists(Path.ChangeExtension(path, ".txt")) ||
                File.Exists(Path.ChangeExtension(path, ".utxt"));
            if (!writeMode && !exists)
                return 0;
            if (writeMode && !Directory.Exists(directory))
                return 0;

            _filePaths.Add(path);
            return (uint)_filePaths.Count;
        }
        public int FileMount(int unit, uint fileIndex, bool writeMode, uint fileOffset)
        {
            const int diskBusy = 16;
            const int noAccess = 8;
            if (unit < 24 || unit >= 56)
                throw new ProcessorException($"Invalid disk unit {Convert.ToString(unit, 8)} in file mount");
            if (_disksByUnit.ContainsKey(unit))
                return diskBusy;
            if (fileIndex == 0 || fileIndex > _filePaths.Count)
                return noAccess;

            string path = _filePaths[(int)fileIndex - 1];
            try
            {
                byte[] data;
                if (File.Exists(path))
                {
                    data = File.ReadAllBytes(path);
                }
                else
                {
                    string textPath = Path.ChangeExtension(path, ".txt");
                    string unicodePath = Path.ChangeExtension(path, ".utxt");
                    if (File.Exists(textPath))
                    {
                        var bytes = new List<byte>();
                        foreach (string line in File.ReadLines(textPath))
                            bytes.AddRange(CosyCodec.EncodeCosy(CosyCodec.Utf8ToKoi7(line)));
                        data = bytes.ToArray();
                    }
                    else if (File.Exists(unicodePath))
                    {
                        data = System.Text.Encoding.ASCII.GetBytes(
                            CosyCodec.Utf8ToKoi7(File.ReadAllText(unicodePath)));
                    }
                    else if (writeMode)
                    {
                        data = Array.Empty<byte>();
                    }
                    else
                    {
                        return noAccess;
                    }
                }

                int minimum = TapeImage.PageNbytes;
                int length = Math.Max(minimum, ((data.Length + 5) / 6) * 6);
                Array.Resize(ref data, length);
                _disksByUnit[unit] = new TapeImage(0, data, readOnly: !writeMode);
                return 0;
            }
            catch (IOException)
            {
                return noAccess;
            }
            catch (UnauthorizedAccessException)
            {
                return noAccess;
            }
        }

        public void ScratchMount(int unit, int zones)
        {
            if (unit < 24 || unit >= 56)
                throw new ProcessorException($"Invalid disk unit {Convert.ToString(unit, 8)} in scratch mount");
            if (_disksByUnit.ContainsKey(unit))
                throw new ProcessorException($"Disk unit {Convert.ToString(unit, 8)} is already mounted");
            _disksByUnit[unit] = new TapeImage(
                0,
                new byte[Math.Max(1, zones) * TapeImage.PageNbytes],
                readOnly: false);
        }

        public int FindUnitForTapeId(long tapeId)
        {
            if (_disksByTapeId.TryGetValue(tapeId, out var disk))
            {
                foreach (var kv in _disksByUnit)
                    if (ReferenceEquals(kv.Value, disk))
                        return kv.Key;
            }
            return 0;
        }

        public bool IsMonsysMounted()
        {
            return _disksByUnit.TryGetValue(24, out TapeImage? mounted) &&
                   mounted.VolumeId == TapeImage.TapeMonsys &&
                   _fileBackedTapes.Contains(mounted);
        }

        public void MountScriptTapes(DubJob job)
        {
            MountRequestedTapes(job);
            EnsureMonsysTape();
        }

        public void MountRequestedTapes(DubJob job)
        {
            if (job.TapeMounts == null || job.TapeMounts.Count == 0) return;
            foreach (var mount in job.TapeMounts)
            {
                long tapeId = TapeImage.TapeIdByName(mount.Name, mount.Channel);
                if (tapeId == 0)
                    throw new ProcessorException($"Unknown tape '{mount.Name}' on channel {Convert.ToString(mount.Channel, 8)}");

                int unit = 24 + (mount.Channel & 0x1F);
                if (!MountRequiredTape(unit, tapeId))
                    throw new ProcessorException(
                        $"Cannot mount tape '{mount.Name}' (0x{tapeId:X12}) on unit {Convert.ToString(unit, 8)} from '{_tapesDir ?? TapeImage.DefaultTapesDir()}'");
            }
        }

        private void EnsureMonsysTape()
        {
            if (IsMonsysMounted()) return;
            if (!MountRequiredTape(24, TapeImage.TapeMonsys))
                throw new ProcessorException(
                    $"Cannot mount MONSYS tape (0x{TapeImage.TapeMonsys:X12}) on unit 30 from '{_tapesDir ?? TapeImage.DefaultTapesDir()}'");
        }

        private bool MountRequiredTape(int unit, long tapeId)
        {
            if (_disksByUnit.TryGetValue(unit, out TapeImage? mounted) &&
                mounted.VolumeId == tapeId &&
                _fileBackedTapes.Contains(mounted))
                return true;

            string? path = TapeImage.FindImagePath(tapeId, _tapesDir);
            if (path == null) return false;
            return MountFileBackedTape(unit, tapeId, path, false);
        }

        public TapeImage? GetDiskByUnit(int unit) =>
            _disksByUnit.TryGetValue(unit, out var d) ? d : null;

        public TapeImage? GetDiskByTapeId(long tapeId) =>
            _disksByTapeId.TryGetValue(tapeId, out var d) ? d : null;

        /// <summary>Count of file-backed tapes (for testing provenance).</summary>
        internal int FileBackedTapeCount => _fileBackedTapes.Count;

        private static string IsoFilename(ulong word)
        {
            Span<char> chars = stackalloc char[6];
            int length = 0;
            for (int shift = 40; shift >= 0; shift -= 8)
            {
                char ch = (char)((word >> shift) & 0x7F);
                chars[length++] = ch == '\0' ? ' ' : char.ToLowerInvariant(ch);
            }
            return new string(chars[..length]).TrimEnd();
        }
    }
}