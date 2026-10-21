using Besm6.AspNetCore.Web.Protocol;
using Besm6.AspNetCore.Web.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace Besm6.AspNetCore.Web.Hubs;

public sealed class EmulatorHub(SessionRegistry sessions) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var session = await sessions.CreateAsync(Context.ConnectionId);
        await Clients.Caller.SendAsync("sessionReady", session.Id);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await sessions.RemoveAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task Upload(byte[] content, string fileName)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".dub", StringComparison.OrdinalIgnoreCase))
            throw new HubException("Select a .dub file.");
        if (content.Length is 0 or > 10_000_000) throw new HubException("File must be between 1 byte and 10 MB.");
        var session = sessions.Get(Context.ConnectionId);
        await File.WriteAllBytesAsync(Path.Combine(session.Directory, "job.dub"), content);
        await Clients.Caller.SendAsync("uploaded", fileName);
    }

    public Task Command(WorkerCommand command)
    {
        if (command.Type == "start")
            command = command with { JobPath = Path.Combine(sessions.Get(Context.ConnectionId).Directory, "job.dub") };
        return sessions.Get(Context.ConnectionId).Worker.SendAsync(command);
    }
}
