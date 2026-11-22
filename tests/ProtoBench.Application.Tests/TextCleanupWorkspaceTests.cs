using ProtoBench.Desktop.Features.Text;

namespace ProtoBench.Application.Tests;

public sealed class TextCleanupWorkspaceTests
{
    [Fact]
    public async Task CleanupKeepsOriginalAndClearsStaleResultsWhenOptionsChange()
    {
        using var workspace = new TextCleanupWorkspace { Input = " A\tB\r\nC ", Tabs = true, LineEndings = true };
        await workspace.CleanCommand.ExecuteAsync(null);
        Assert.Equal("ABC", workspace.Output);
        Assert.Equal(" A\tB\r\nC ", workspace.Input);
        Assert.True(workspace.HasResult);
        Assert.Contains("5", workspace.Summary);
        workspace.CustomCharacters = "ABC";
        Assert.False(workspace.HasResult);
        Assert.Empty(workspace.Output);
        await workspace.CleanCommand.ExecuteAsync(null);
        Assert.True(workspace.HasResult);
        Assert.Empty(workspace.Output);
        workspace.ClearCommand.Execute(null);
        Assert.False(workspace.HasResult);
        Assert.Empty(workspace.Input);
        Assert.Empty(workspace.Summary);
    }
}
