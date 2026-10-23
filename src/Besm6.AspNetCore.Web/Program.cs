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
builder.Services.AddSingleton<Besm6.AspNetCore.Web.Hosting.ExampleCatalog>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionRegistry>());
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<EmulatorHub>("/emulator");
app.MapGet("/api/punchcards/codebook", () => Besm6.AspNetCore.Web.Hosting.PunchCardCodebook.Data);
app.MapGet("/api/examples", (Besm6.AspNetCore.Web.Hosting.ExampleCatalog examples) => examples.Entries);
app.MapGet("/api/media", () => Besm6.AspNetCore.Web.Hosting.MediaCatalog.Entries);
app.MapGet("/api/machine-template", () => Results.Text(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "machine-template.json")), "application/json"));
app.MapGet("/api/instructions", () => Besm6.Assembler.Disassembly.InstructionEditor.Operations);
app.MapPost("/api/instructions/decode", (Besm6.AspNetCore.Web.Hosting.InstructionRequest request) => Besm6.AspNetCore.Web.Hosting.InstructionEndpoints.Convert(request, false));
app.MapPost("/api/instructions/encode", (Besm6.AspNetCore.Web.Hosting.InstructionRequest request) => Besm6.AspNetCore.Web.Hosting.InstructionEndpoints.Convert(request, true));
app.MapGet("/api/examples/source", (string id, Besm6.AspNetCore.Web.Hosting.ExampleCatalog examples) =>
    examples.TryGetPath(id, out string path) ? Results.Text(File.ReadAllText(path), "text/plain", System.Text.Encoding.UTF8) : Results.NotFound());
app.Run();

public partial class Program { }
