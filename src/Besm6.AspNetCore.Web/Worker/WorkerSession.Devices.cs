using System.Globalization;
using System.Threading.Channels;
using Besm6.Architecture.Numerics;
using Besm6.AspNetCore.Web.Hosting;
using Besm6.AspNetCore.Web.Protocol;
using Besm6.Assembler.Disassembly;
using Besm6.Runtime.Devices;
using Besm6.Runtime.Encoding;
using Besm6.Runtime.Loading;
using Besm6.Runtime.Storage;

namespace Besm6.AspNetCore.Web.Worker;

public sealed partial class WorkerSession
{
    private sealed record Attachment(string Kind, int Unit, TapeImage Image, string? BuiltinId);
    private readonly Dictionary<(string Kind, int Unit), Attachment> _attachments = new();
    private sealed record TerminalLine(string Text, TeletypeDevice? Device = null);
    private readonly Channel<TerminalLine> _terminalInput = Channel.CreateBounded<TerminalLine>(64);
    private readonly AutoResetEvent _inputAvailable = new(false);
    private volatile bool _waitingForInput;

    private async Task DeviceCommandAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or FormatException or OverflowException)
        {
            // An invalid inspection request must not change a paused/running machine's state.
            await SendAsync("commandError", new ErrorData(ex.Message, null));
        }
    }

    private void AttachDeviceSinks()
    {
        if (_machine!.Devices.GetDevice(0x4000) is TeletypeDevice tty)
            tty.Output = text => _output.Append("terminal", text);
        _machine.Puncher.CardPunched += card =>
        {
            var rows = Enumerable.Range(0, 12).Select(row => new string(Enumerable.Range(0, 80)
                .Select(column => card.IsPunched(row, column) ? '1' : '0').ToArray())).ToArray();
            SendAsync("punchcard", new PunchCardData(rows)).GetAwaiter().GetResult();
        };
    }

    private Task AcceptInputAsync(WorkerCommand command)
    {
        if (command.Text is null || command.Text.Length > 4096 || command.Text.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw new ArgumentException("Enter one line up to 4096 characters without NUL.");
        if (_standalone)
        {
            _machine!.SupervisorIo!.ReceiveSerial(1, System.Text.Encoding.Latin1.GetBytes(CosyCodec.Utf8ToKoi7(command.Text + "\n")));
            return SendAsync("inputAccepted", new { target = "serial:1" });
        }
        if (_terminalInput.Reader.Count >= 64) throw new InvalidOperationException("Terminal input queue is full.");
        TeletypeDevice? device = null;
        if (command.InputTarget == "teletype")
        {
            if (_machine?.Devices.GetDevice(0x4000) is not TeletypeDevice tty)
                throw new InvalidOperationException("Load a machine before sending teletype input.");
            tty.SendInput(command.Text + "\n");
            device = tty;
        }
        else if (command.InputTarget is not (null or "terminal")) throw new ArgumentException("Unknown terminal input target.");
        if (!_terminalInput.Writer.TryWrite(new(command.Text, device))) throw new InvalidOperationException("Terminal input queue is full.");
        _inputAvailable.Set();
        return SendAsync("inputAccepted", new { target = command.InputTarget ?? "terminal", waiting = _waitingForInput });
    }

    private void WriteTerminalOutput(string text)
    {
        if (_machine?.Devices.GetDevice(0x4000) is TeletypeDevice tty)
            foreach (char character in text) tty.WriteCharacter(new Word48(character));
        else _output.Append("terminal", text);
    }

    private static string ConsumeTerminalLine(TerminalLine line)
    {
        if (line.Device is null) return line.Text;
        var text = new System.Text.StringBuilder();
        for (int i = 0; i <= line.Text.Length; i++)
        {
            char character = (char)line.Device.ReadCharacter().Value;
            if (character is '\n' or '\0') break;
            text.Append(character);
        }
        return text.ToString();
    }

    private string ReadTerminalInput(string prompt)
    {
        var token = _runCancellation!.Token;
        if (_terminalInput.Reader.TryRead(out var queued)) return ConsumeTerminalLine(queued);
        _waitingForInput = true;
        try
        {
            _output.FlushAsync().GetAwaiter().GetResult();
            SendSnapshotAsync().GetAwaiter().GetResult();
            SendAsync("inputRequest", new { prompt }).GetAwaiter().GetResult();
            SendStateAsync("WaitingInput").GetAwaiter().GetResult();
            TerminalLine? line;
            while (!_terminalInput.Reader.TryRead(out line))
            {
                // Keep the same safe pause/snapshot boundary while a host read is pending.
                ExecutionBoundary(token);
                _inputAvailable.WaitOne(50);
            }
            ExecutionBoundary(token);
            string text = ConsumeTerminalLine(line);
            _waitingForInput = false;
            SendStateAsync("Running").GetAwaiter().GetResult();
            return text;
        }
        finally { _waitingForInput = false; }
    }

    private void ClearInput()
    {
        while (_terminalInput.Reader.TryRead(out _)) { }
    }

    private void RequirePausedDevices()
    {
        if (_standalone) throw new InvalidOperationException("Supervisor media are configured in machine.json. Hosted Storage controls are for .dub jobs.");
        if (!_terminal && _runTask is { IsCompleted: false } && _resume.IsSet)
            throw new InvalidOperationException("Pause or stop execution before inspecting or mounting media.");
    }

    private void EnsureMediaLoader()
    {
        if (_loader is not null) return;
        _machine ??= new Besm6.Runtime.MachineCore(puncherOutputDir: _directory);
        _loader = new DubnaLoader(_machine, Path.Combine(AppContext.BaseDirectory, "tapes")) { HostFileDirectory = _directory };
        foreach (var attachment in _attachments.Values) ApplyAttachment(attachment);
    }

    private void ApplyAttachment(Attachment attachment)
    {
        if (attachment.BuiltinId is not null && attachment.Kind != "Drum")
        {
            _loader!.UnmountImage(attachment.Kind, attachment.Unit);
            if (!_loader.MountTape(attachment.Unit, attachment.Image.VolumeId, !attachment.Image.ReadOnly))
                throw new InvalidOperationException("Cannot mount runtime tape.");
        }
        else _loader!.MountImage(attachment.Kind, attachment.Unit, attachment.Image, replace: true);
    }

    private TapeImage ResolveMedia(string id)
    {
        if (MediaCatalog.TryGet(id, out string path, out long volume)) return TapeImage.LoadFromFile(volume, path);
        if (id.StartsWith("upload:", StringComparison.Ordinal) && Guid.TryParseExact(id[7..], "N", out var upload))
            return TapeImage.LoadFromFile(0, Path.Combine(_directory, $"media-{upload:N}.bin"));
        string[] parts = id.Split(':');
        if (parts.Length == 3 && parts[0] == "mounted" && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int unit))
        {
            EnsureMediaLoader();
            return _loader!.GetMountedImage(parts[1], unit) ?? throw new InvalidOperationException("No image is mounted on that unit.");
        }
        throw new ArgumentException("Select a built-in, uploaded or mounted image.");
    }

    private async Task HandleMediaAsync(WorkerCommand command)
    {
        RequirePausedDevices();
        EnsureMediaLoader();
        if (command.Type == "unmount")
        {
            _loader!.UnmountImage(command.Kind, command.Unit);
            _attachments.Remove((command.Kind == "Tape" ? "Disk" : command.Kind, command.Unit));
            await SendSnapshotAsync();
            return;
        }
        string id = command.MediaId ?? throw new ArgumentException("Select a media image.");
        var source = ResolveMedia(id);
        if (command.Type == "mount")
        {
            if (!long.TryParse(command.Volume, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long volume) || volume is < 0 or > 0xFFFFFFFFFFFF)
                throw new ArgumentException("Volume ID must be a 48-bit hexadecimal number.");
            if (command.Kind is not ("Disk" or "Tape" or "Drum")) throw new ArgumentException("Select Disk, Tape or Drum.");
            bool validUnit = command.Kind == "Drum" ? command.Unit is >= 0 and <= 31 : command.Unit is >= 24 and <= 55;
            if (!validUnit) throw new ArgumentException("Disk/tape units: 30–67 OCT. Drum units: 0–37 OCT.");
            if (!command.Replace && _loader!.GetMountedImage(command.Kind, command.Unit) is not null)
                throw new InvalidOperationException("Unit is already mounted. Select Replace to replace it.");
            string? builtin = id.StartsWith("builtin:", StringComparison.Ordinal) ? id : null;
            var image = new TapeImage(builtin is null ? volume : source.VolumeId, (byte[])source.Data.Clone(), command.ReadOnly);
            var attachment = new Attachment(command.Kind, command.Unit, image, builtin);
            ApplyAttachment(attachment);
            _attachments[(command.Kind == "Tape" ? "Disk" : command.Kind, command.Unit)] = attachment;
            await SendSnapshotAsync();
            return;
        }
        if (command.MediaOffset < 0 || command.MediaCount is < 1 or > 128 || command.MediaOffset >= source.NumWords)
            throw new ArgumentException("Select an existing word offset and 1–128 words.");
        int count = Math.Min(command.MediaCount, source.NumWords - command.MediaOffset);
        int? loadedAddress = null;
        if (command.Type == "mediaToMemory")
        {
            if (command.MemoryAddress < 0 || command.MemoryAddress > _machine!.Memory.Size - count)
                throw new ArgumentException("The destination range must fit in machine memory.");
            for (int i = 0; i < count; i++) _machine.Memory.Write((uint)(command.MemoryAddress + i), new Word48((ulong)source.ReadWord(command.MediaOffset + i)));
            loadedAddress = command.MemoryAddress;
            _memoryAddress = command.MemoryAddress;
            await SendSnapshotAsync();
        }
        var words = Enumerable.Range(command.MediaOffset, count).Select(offset =>
        {
            ulong word = (ulong)source.ReadWord(offset);
            string text = new(Enumerable.Range(0, 6).Select(i =>
            {
                byte code = (byte)(word >> (40 - i * 8));
                return code is >= 32 and < 128 ? CosyCodec.Koi7ToUnicode(code) : '·';
            }).ToArray());
            return new MediaWord(offset, word.ToString("X12"), Disassembler.DisasmWord(word), text);
        }).ToArray();
        await SendAsync("mediaView", new MediaViewData(id, source.NumWords, words, loadedAddress));
    }
}
