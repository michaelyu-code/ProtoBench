using ProtoBench.Application.Workspaces;
using ProtoBench.Infrastructure.Plugins;

namespace ProtoBench.Desktop.Features.Networking;

public sealed class TestTelnetServerWorkspaceFactory(PluginCatalog plugins) : IToolWorkspaceFactory
{
    public string ToolId => "test-telnet-server";

    // WorkspaceManager owns each tab; supply exports without resolving a DI-owned transient.
    public ToolWorkspace Create() => new TestTelnetServerWorkspace(plugins.Plugins.SelectMany(p => p.TelnetServers));
}
