using System.Diagnostics;
using System.IO.Pipes;
using Besm6.AspNetCore.Web.Protocol;

namespace Besm6.AspNetCore.Web.Hosting;

public sealed class WorkerProcessHost : IAsyncDisposable
{
    private readonly WorkerOptions _options;
    private readonly Func<WorkerEvent, Task> _onEvent;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Process? _process;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Task? _readTask;
    private bool _disposing;

    public WorkerProcessHost(WorkerOptions options, Func<WorkerEvent, Task> onEvent)
    {
        _options = options;
        _onEvent = onEvent;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_options.SessionDirectory);
        // Unix named pipes are sockets under TMPDIR. The worker has a private
        // TMPDIR, so both processes must use the same absolute socket path.
        string pipeName = OperatingSystem.IsWindows() ? _options.PipeName
            : Path.Combine(Path.GetFullPath(_options.SessionDirectory), "worker.pipe");
        string assembly = typeof(WorkerProcessHost).Assembly.Location;
        string? appHost = Environment.ProcessPath;
        bool ownAppHost = appHost is not null &&
            string.Equals(Path.GetFileNameWithoutExtension(appHost), "Besm6.AspNetCore.Web", StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo(ownAppHost ? appHost! : "dotnet")
        {
            WorkingDirectory = _options.SessionDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        if (!ownAppHost) start.ArgumentList.Add(assembly);
        start.ArgumentList.Add("--worker");
        start.ArgumentList.Add("--pipe");
        start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add(_options.SessionDirectory);
        start.Environment["TEMP"] = _options.SessionDirectory;
        start.Environment["TMP"] = _options.SessionDirectory;
        start.Environment["TMPDIR"] = _options.SessionDirectory;
        _process = Process.Start(start) ?? throw new InvalidOperationException("Worker process did not start.");
        _ = _process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        _ = _process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await _pipe.ConnectAsync(timeout.Token);
        _reader = new StreamReader(_pipe, leaveOpen: true);
        _writer = new StreamWriter(_pipe, leaveOpen: true) { AutoFlush = true };
        _readTask = ReadEventsAsync();
    }

    public async Task SendAsync(WorkerCommand command)
    {
        if (_writer is null) throw new InvalidOperationException("Worker is not connected.");
        await _writeLock.WaitAsync();
        try { await _writer.WriteLineAsync(WorkerJson.Serialize(command)); }
        finally { _writeLock.Release(); }
    }

    private async Task ReadEventsAsync()
    {
        try
        {
            while (await _reader!.ReadLineAsync() is { } line)
            {
                var value = WorkerJson.Deserialize<WorkerEvent>(line);
                if (value is not null) await _onEvent(value);
            }
            if (!_disposing)
            {
                await _onEvent(new WorkerEvent("error", new ErrorData("Worker disconnected unexpectedly.", null)));
                await _onEvent(new WorkerEvent("state", new StateData("Failed")));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or System.Text.Json.JsonException)
        {
            if (!_disposing)
            {
                await _onEvent(new WorkerEvent("error", new ErrorData(ex.Message, null)));
                await _onEvent(new WorkerEvent("state", new StateData("Failed")));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposing) return;
        _disposing = true;
        Task? disposeCommand = _writer is not null ? SendAsync(new WorkerCommand("dispose")) : null;
        try { if (disposeCommand is not null) await disposeCommand.WaitAsync(TimeSpan.FromMilliseconds(500)); } catch { }
        try
        {
            if (_process is { HasExited: false })
            {
                using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await _process.WaitForExitAsync(wait.Token); }
                catch (OperationCanceledException)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync();
                }
            }
        }
        catch (InvalidOperationException) { }
        _pipe?.Dispose();
        try { if (disposeCommand is not null) await disposeCommand.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
        try { _writer?.Dispose(); } catch (ObjectDisposedException) { } catch (IOException) { }
        try { _reader?.Dispose(); } catch (ObjectDisposedException) { } catch (IOException) { }
        if (_readTask is not null) try { await _readTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        _process?.Dispose();
        _writeLock.Dispose();
    }
}
