using System.Text;

namespace Besm6.Android.Services;

/// <summary>Bounded per-device text retained for dialogs reopened after output has arrived.</summary>
public sealed class DeviceTextBuffer
{
    public const int MaxChars = 512 * 1024;
    private const int TrimChars = 128 * 1024;

    private readonly Dictionary<string, StringBuilder> _buffers = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Returns true when the oldest text had to be discarded.</summary>
    public bool Append(string channel, string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        lock (_gate)
        {
            if (!_buffers.TryGetValue(channel, out var buffer))
            {
                buffer = new StringBuilder();
                _buffers[channel] = buffer;
            }
            buffer.Append(text);
            if (buffer.Length <= MaxChars) return false;
            buffer.Remove(0, Math.Min(TrimChars, buffer.Length));
            return true;
        }
    }

    public string Get(string channel)
    {
        lock (_gate)
            return _buffers.TryGetValue(channel, out var buffer) ? buffer.ToString() : string.Empty;
    }

    public void Clear()
    {
        lock (_gate) _buffers.Clear();
    }
}
