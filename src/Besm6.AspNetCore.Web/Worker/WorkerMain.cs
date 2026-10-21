using System.IO.Pipes;
using Besm6.AspNetCore.Web.Protocol;

namespace Besm6.AspNetCore.Web.Worker;

public static class WorkerMain
{
    public static async Task RunAsync(string pipeName, string sessionDirectory)
    {
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await pipe.WaitForConnectionAsync();
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var session = new WorkerSession(sessionDirectory, async value =>
        {
            await writer.WriteLineAsync(WorkerJson.Serialize(value));
        });
        await session.SendStateAsync("Idle");
        while (await reader.ReadLineAsync() is { } line)
        {
            try
            {
                var command = WorkerJson.Deserialize<WorkerCommand>(line)
                    ?? throw new InvalidDataException("Empty command.");
                if (command.Type == "dispose") break;
                await session.HandleAsync(command);
            }
            catch (Exception ex)
            {
                await session.SendErrorAsync(ex);
            }
        }
    }
}
