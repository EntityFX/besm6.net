using Besm6.AspNetCore.Web.Hosting;

namespace Besm6.AspNetCore.Web.Sessions;

public sealed record SessionState(string Id, string Directory, WorkerProcessHost Worker);
