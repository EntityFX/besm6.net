using Besm6.AspNetCore.Web.Protocol;
using Besm6.AspNetCore.Web.Sessions;
using Microsoft.AspNetCore.SignalR;
using Besm6.AspNetCore.Web.Hosting;

namespace Besm6.AspNetCore.Web.Hubs;

public sealed class EmulatorHub(SessionRegistry sessions, ExampleCatalog examples) : Hub
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

    public async Task LoadExample(string id)
    {
        if (!examples.TryGetPath(id, out string path)) throw new HubException("Example not found.");
        var session = sessions.Get(Context.ConnectionId);
        await File.WriteAllBytesAsync(Path.Combine(session.Directory, "job.dub"), await File.ReadAllBytesAsync(path));
        await Clients.Caller.SendAsync("uploaded", id);
    }

    public async Task UploadText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || System.Text.Encoding.UTF8.GetByteCount(text) > 1_000_000)
            throw new HubException("Enter a .dub program up to 1 MB.");
        var session = sessions.Get(Context.ConnectionId);
        await File.WriteAllTextAsync(Path.Combine(session.Directory, "job.dub"), text, new System.Text.UTF8Encoding(false));
        await Clients.Caller.SendAsync("uploaded", "editor.dub");
    }

    public async Task UploadMedia(byte[] content, string fileName)
    {
        if (content.Length is 0 or > 8_000_000 || content.Length % 6 != 0)
            throw new HubException("Use a dense image of 48-bit words (6 bytes each), up to 8 MB.");
        string id = Guid.NewGuid().ToString("N");
        var session = sessions.Get(Context.ConnectionId);
        await File.WriteAllBytesAsync(Path.Combine(session.Directory, "media-" + id + ".bin"), content);
        await Clients.Caller.SendAsync("mediaUploaded", new MediaEntry("upload:" + id, Path.GetFileName(fileName), "000000000000", content.Length / 6));
    }
}
