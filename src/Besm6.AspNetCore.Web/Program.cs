using Besm6.AspNetCore.Web.Hubs;
using Besm6.AspNetCore.Web.Sessions;

if (args.Length == 4 && args[0] == "--worker" && args[1] == "--pipe")
{
    await Besm6.AspNetCore.Web.Worker.WorkerMain.RunAsync(args[2], args[3]);
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR(options => options.MaximumReceiveMessageSize = 16 * 1024 * 1024);
builder.Services.AddSingleton<SessionRegistry>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionRegistry>());
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<EmulatorHub>("/emulator");
app.Run();

public partial class Program { }
