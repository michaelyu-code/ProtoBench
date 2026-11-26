using System.Text.Json;
using ProtoBench.Application.Tools;
using ProtoBench.Application.Workspaces;
using ProtoBench.PluginSdk;

namespace ProtoBench.Application.Tests;

public sealed class PluginWebViewTests
{
    [Fact]
    public void ExistingToolsDefaultToCustomMenuAndKeepTheirIcon()
    {
        var tool = Tool();
        var descriptor = ToolDescriptor.FromPlugin(tool);
        Assert.Equal(ToolCategory.CustomTools, descriptor.Category);
        Assert.True(descriptor.IsCustom);
        Assert.Equal(tool.Icon, descriptor.Icon);
        Assert.Equal("Custom tool", descriptor.CustomLabel);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public async Task UnknownMenuFallsBackWithoutChangingToolIdentityOrExecution(int category)
    {
        var tool = Tool() with { Category = (PluginToolCategory)category };
        var descriptor = ToolDescriptor.FromPlugin(tool);
        Assert.Equal(ToolCategory.CustomTools, descriptor.Category);
        Assert.True(descriptor.IsCustom);
        Assert.Equal(tool.Id, descriptor.Id);
        Assert.Equal(tool.Name, descriptor.Name);
        Assert.Equal(tool.Icon, descriptor.Icon);
        using var workspace = new PluginToolWorkspace(tool);
        workspace.SetInput("value", "still available");
        await workspace.ExecuteAsync();
        Assert.Equal("still available", workspace.Output);
    }

    [Theory]
    [InlineData(PluginToolCategory.Parsers, ToolCategory.Parsers)]
    [InlineData(PluginToolCategory.Converters, ToolCategory.Converters)]
    [InlineData(PluginToolCategory.Generators, ToolCategory.Generators)]
    [InlineData(PluginToolCategory.Comparisons, ToolCategory.Comparisons)]
    [InlineData(PluginToolCategory.Utilities, ToolCategory.Utilities)]
    [InlineData(PluginToolCategory.Payments, ToolCategory.Payments)]
    [InlineData(PluginToolCategory.Calculators, ToolCategory.Calculators)]
    public void PluginMenusRetainCustomProvenance(PluginToolCategory pluginCategory, ToolCategory category)
    {
        var descriptor = ToolDescriptor.FromPlugin(Tool() with { Category = pluginCategory });
        Assert.Equal(category, descriptor.Category);
        Assert.True(descriptor.IsCustom);
    }

    [Fact]
    public async Task WebExecutionUsesValidationAndPerTabSessions()
    {
        using var first = new PluginToolWorkspace(Tool());
        using var second = new PluginToolWorkspace(Tool());
        var bridge = new PluginWebViewBridge(first);
        using var invalid = JsonDocument.Parse(await bridge.ReceiveAsync("""{"id":"1","type":"execute","inputs":{}}"""));
        Assert.False(invalid.RootElement.GetProperty("success").GetBoolean());
        Assert.Null(first.Result);
        using var valid = JsonDocument.Parse(await bridge.ReceiveAsync("""{"id":"2","type":"execute","inputs":{"value":"hello"}}"""));
        Assert.True(valid.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("2", valid.RootElement.GetProperty("id").GetString());
        Assert.Equal("hello", valid.RootElement.GetProperty("result").GetProperty("text").GetString());
        Assert.Null(second.Result);
        Assert.Empty(second.Inputs["value"]);
        await bridge.ReceiveAsync("""{"id":"3","type":"clear"}""");
        Assert.Null(first.Result);
        Assert.Empty(first.Inputs["value"]);
    }

    [Theory]
    [InlineData("invalid JSON")]
    [InlineData("[]")]
    [InlineData("""{"id":"1","type":"execute","inputs":{"value":"changed","unknown":"bad"}}""")]
    [InlineData("""{"id":"1","type":"execute","inputs":{"value":null}}""")]
    [InlineData("""{"id":"1","type":"execute","inputs":{"value":7}}""")]
    [InlineData("""{"id":"1","type":"unsupported"}""")]
    public async Task InvalidMessagesDoNotMutateInputs(string message)
    {
        using var workspace = new PluginToolWorkspace(Tool());
        using var response = JsonDocument.Parse(await new PluginWebViewBridge(workspace).ReceiveAsync(message));
        Assert.False(response.RootElement.GetProperty("success").GetBoolean());
        Assert.Empty(workspace.Inputs["value"]);
        Assert.Null(workspace.Result);
    }

    [Fact]
    public async Task CancellationAndClosingWaitForSessionExecution()
    {
        var session = new WaitingSession();
        using var workspace = new PluginToolWorkspace(Tool() with { CreateSession = () => session });
        var bridge = new PluginWebViewBridge(workspace);
        var execution = bridge.ReceiveAsync("""{"id":"run","type":"execute","inputs":{"value":"hello"}}""");
        await session.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var busy = JsonDocument.Parse(await bridge.ReceiveAsync("""{"id":"busy","type":"execute","inputs":{"value":"changed"}}"""));
        Assert.False(busy.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("hello", workspace.Inputs["value"]);
        await bridge.ReceiveAsync("""{"id":"cancel","type":"cancel"}""");
        var closed = 0;
        workspace.Disposed += (_, _) => closed++;
        workspace.Dispose();
        await execution;
        Assert.True(session.Disposed);
        Assert.Equal(1, closed);
        Assert.Null(workspace.Result);
    }

    private static PluginTool Tool() => new("test.web", "Test", "Test tool", [new("value", "Value", Required: true)], () => new EchoSession());

    private sealed class EchoSession : IPluginToolSession
    {
        public Task<PluginToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> inputs, CancellationToken token) => Task.FromResult(new PluginToolResult(inputs["value"]));
        public void Dispose() { }
    }

    private sealed class WaitingSession : IPluginToolSession
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public async Task<PluginToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> inputs, CancellationToken token)
        {
            Started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new("unreachable");
        }
        public void Dispose() => Disposed = true;
    }
}
