using Besm6.AspNetCore.Web.Hubs;
using Besm6.AspNetCore.Web.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace Besm6.AspNetCore.Web.Tests;

[TestClass]
public sealed class SessionRegistryTests
{
    [TestMethod]
    public async Task DisconnectAndShutdownRemoveWorkerDirectories()
    {
        var registry = new SessionRegistry(new TestHubContext());
        var first = await registry.CreateAsync("first");
        var second = await registry.CreateAsync("second");
        Assert.IsTrue(Directory.Exists(first.Directory));
        Assert.IsTrue(Directory.Exists(second.Directory));
        await registry.RemoveAsync("first");
        Assert.IsFalse(Directory.Exists(first.Directory));
        await registry.StopAsync(CancellationToken.None);
        Assert.IsFalse(Directory.Exists(second.Directory));
    }

    private sealed class TestHubContext : IHubContext<EmulatorHub>
    {
        public IHubClients Clients { get; } = new TestHubClients();
        public IGroupManager Groups { get; } = new TestGroupManager();
    }

    private sealed class TestHubClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new TestClientProxy();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class TestClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class TestGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
