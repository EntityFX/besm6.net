using System.Collections.Concurrent;
using Besm6.AspNetCore.Web.Hosting;
using Besm6.AspNetCore.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Besm6.AspNetCore.Web.Sessions;

public sealed class SessionRegistry : IHostedService
{
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();
    private readonly IHubContext<EmulatorHub> _hub;
    private readonly string _root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "besm6-web"));

    public SessionRegistry(IHubContext<EmulatorHub> hub) => _hub = hub;

    public async Task<SessionState> CreateAsync(string connectionId)
    {
        string id = Guid.NewGuid().ToString("N");
        string directory = Path.Combine(_root, id);
        var worker = new WorkerProcessHost(new WorkerOptions(directory, "besm6-" + id),
            value => _hub.Clients.Client(connectionId).SendAsync("workerEvent", value));
        var session = new SessionState(id, directory, worker);
        if (!_sessions.TryAdd(connectionId, session)) throw new InvalidOperationException("Session exists.");
        try
        {
            await worker.StartAsync();
            return session;
        }
        catch
        {
            await RemoveAsync(connectionId);
            throw;
        }
    }

    public SessionState Get(string connectionId) => _sessions.TryGetValue(connectionId, out var session)
        ? session : throw new HubException("Session is unavailable.");

    public async Task RemoveAsync(string connectionId)
    {
        if (!_sessions.TryRemove(connectionId, out var session)) return;
        await session.Worker.DisposeAsync();
        string full = Path.GetFullPath(session.Directory);
        string root = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid session directory.");
        for (int attempt = 0; attempt < 10 && Directory.Exists(full); attempt++)
        {
            try { Directory.Delete(full, recursive: true); }
            catch (IOException) when (attempt < 9) { await Task.Delay(100); }
            catch (UnauthorizedAccessException) when (attempt < 9) { await Task.Delay(100); }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var id in _sessions.Keys) await RemoveAsync(id);
    }
}
