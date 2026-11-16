using System.Net;
using System.Net.Sockets;
using ProtoBench.Desktop.Features.Networking;

namespace ProtoBench.Application.Tests;

public sealed class TestTelnetServerWorkspaceTests
{
    [Theory]
    [InlineData(ProtoBench.Utilities.Networking.TelnetServerProfile.Ascii, true)]
    [InlineData(ProtoBench.Utilities.Networking.TelnetServerProfile.AnsiVt100, false)]
    [InlineData(ProtoBench.Utilities.Networking.TelnetServerProfile.TelservTn6530, false)]
    public async Task ProfileStartsAndUpdatesDescriptionAndOptions(ProtoBench.Utilities.Networking.TelnetServerProfile profile, bool showAnsi)
    {
        using var workspace = new TestTelnetServerWorkspace { ListenPort = "0", Profile = profile };
        Assert.Equal(showAnsi, workspace.ShowAnsiGreeting);
        Assert.NotEmpty(workspace.ProfileDescription);
        workspace.Start();
        Assert.True(workspace.IsRunning); Assert.Contains($"profile: {profile}", workspace.Activity);
        await workspace.StopAsync();
    }

    [Fact]
    public async Task ValidatesStartsOnAutomaticPortStopsAndRestarts()
    {
        using var workspace = new TestTelnetServerWorkspace { ListenAddress = "bad", ListenPort = "0" };
        workspace.Start(); Assert.False(workspace.IsRunning); Assert.NotEmpty(workspace.Error);
        workspace.ListenAddress = "127.0.0.1"; workspace.ListenPort = "65536";
        workspace.Start(); Assert.False(workspace.IsRunning); Assert.Contains("port", workspace.Error);
        workspace.ListenPort = "0"; workspace.Start();
        Assert.True(workspace.IsRunning); Assert.False(workspace.CanConfigure); Assert.True(workspace.CanStop);
        var endpoint = IPEndPoint.Parse(workspace.Endpoint);
        Assert.NotEqual(0, endpoint.Port);
        Assert.Contains("Listening", workspace.Activity);
        await workspace.StopAsync();
        Assert.False(workspace.IsRunning); Assert.True(workspace.CanConfigure); Assert.False(workspace.CanStop);
        using (var rebound = new TcpListener(endpoint)) rebound.Start();
        workspace.Start(); Assert.True(workspace.IsRunning);
        workspace.ClearActivity(); Assert.Empty(workspace.Activity);
        await workspace.StopAsync();
    }

    [Fact]
    public void ClosingWorkspaceReleasesListenerAndPreventsRestart()
    {
        var workspace = new TestTelnetServerWorkspace { ListenPort = "0" };
        workspace.Start(); Assert.True(workspace.IsRunning);
        var endpoint = IPEndPoint.Parse(workspace.Endpoint);
        workspace.Dispose();
        Assert.False(workspace.IsRunning); Assert.False(workspace.CanConfigure); Assert.False(workspace.CanStop);
        using var rebound = new TcpListener(endpoint); rebound.Start();
        workspace.Start(); Assert.False(workspace.IsRunning);
    }

    [Fact]
    public void OccupiedPortReportsErrorAndRemainsConfigurable()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var workspace = new TestTelnetServerWorkspace { ListenPort = ((IPEndPoint)listener.LocalEndpoint).Port.ToString() };
        workspace.Start();
        Assert.False(workspace.IsRunning); Assert.True(workspace.CanConfigure); Assert.NotEmpty(workspace.Error);
    }
}
