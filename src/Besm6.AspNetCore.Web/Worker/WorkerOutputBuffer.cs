using System.Text;

namespace Besm6.AspNetCore.Web.Worker;

/// <summary>Keep pipe writes and small output messages outside the guest execution loop.</summary>
internal sealed class WorkerOutputBuffer : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, StringBuilder> _buffers = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Func<string, string, Task> _send;
    private readonly Task _pump;
    private int _bufferedCharacters;

    internal WorkerOutputBuffer(Func<string, string, Task> send)
    {
        _send = send;
        _pump = PumpAsync();
    }

    internal void Append(string channel, string text)
    {
        bool flush;
        lock (_gate)
        {
            if (!_buffers.TryGetValue(channel, out var buffer)) _buffers[channel] = buffer = new();
            buffer.Append(text);
            _bufferedCharacters += text.Length;
            flush = _bufferedCharacters >= 1_048_576;
        }
        if (flush) FlushAsync().GetAwaiter().GetResult();
    }

    internal async Task FlushAsync()
    {
        await _flushLock.WaitAsync();
        try
        {
            KeyValuePair<string, string>[] chunks;
            lock (_gate)
            {
                chunks = _buffers.Where(x => x.Value.Length > 0)
                    .Select(x => KeyValuePair.Create(x.Key, x.Value.ToString())).ToArray();
                foreach (var buffer in _buffers.Values) buffer.Clear();
                _bufferedCharacters = 0;
            }
            foreach (var chunk in chunks) await _send(chunk.Key, chunk.Value);
        }
        finally { _flushLock.Release(); }
    }

    private async Task PumpAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(_cancellation.Token)) await FlushAsync();
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (IOException) { /* The host detects pipe closure and disposes this worker. */ }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try { _pump.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cancellation.Dispose();
        // A disconnected host may still be unwinding a pipe write.
        if (_pump.IsCompleted) _flushLock.Dispose();
    }
}
