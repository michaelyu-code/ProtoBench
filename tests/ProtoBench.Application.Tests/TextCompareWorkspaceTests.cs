using ProtoBench.Desktop.Features.Text;
using ProtoBench.Utilities.Text;

namespace ProtoBench.Application.Tests;

public sealed class TextCompareWorkspaceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CopySelectedChangeAndUndoPreserveBothInputs(bool toRight)
    {
        using var vm = new TextCompareWorkspace { Original = "a\nold\nend", Modified = "a\nnew\nend" };
        await vm.CompareCommand.ExecuteAsync(null);
        vm.CopyChange(toRight);
        Assert.Equal(vm.Original, vm.Modified);
        Assert.Null(vm.Alignment);
        Assert.True(vm.CanUndoCopy);
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Empty(vm.Alignment!.Changes);
        Assert.Equal(-1, vm.SelectedChange);
        Assert.True(vm.CanUndoCopy);
        vm.UndoCopy();
        Assert.Equal("a\nold\nend", vm.Original);
        Assert.Equal("a\nnew\nend", vm.Modified);
        Assert.False(vm.CanUndoCopy);
    }

    [Fact]
    public async Task CopyPreservesIgnoredTextAndRulesDoNotDiscardUndo()
    {
        using var vm = new TextCompareWorkspace { Original = "A\n\nold\n", Modified = "a\nnew\n", Options = new(true, false, true) };
        await vm.CompareCommand.ExecuteAsync(null);
        vm.CopyChange(true);
        Assert.Equal("a\nold\n", vm.Modified);
        Assert.Equal("A\n\nold\n", vm.Original);
        vm.Options = new();
        Assert.True(vm.CanUndoCopy);
        vm.UndoCopy();
        Assert.Equal("a\nnew\n", vm.Modified);
    }

    [Fact]
    public async Task NavigationWrapsAndEditsInvalidateCopyAndReport()
    {
        using var vm = new TextCompareWorkspace { Original = "old\nsame\nold", Modified = "new\nsame\nnew" };
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Alignment!.Changes.Count);
        vm.Navigate(-1);
        Assert.Equal(1, vm.SelectedChange);
        vm.Navigate(1);
        Assert.Equal(0, vm.SelectedChange);
        vm.CopyChange(true);
        vm.Modified += "edit";
        Assert.False(vm.CanUndoCopy);
        Assert.Null(vm.Alignment);
        Assert.Throws<InvalidOperationException>(() => vm.CreateDifferencesReport());
    }

    [Fact]
    public async Task RulesDistinguishExactMatchFromIgnoredChanges()
    {
        using var vm = new TextCompareWorkspace { Original = "A b", Modified = "ab", Options = new TextCompareOptions(true, true) };
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Equal("Equal under current rules.", vm.Summary);
        Assert.Empty(vm.Alignment!.Changes);
        var report = vm.CreateDifferencesReport();
        Assert.Contains("ignore case, ignore whitespace", report);
        Assert.DoesNotContain("<del>", report);
        Assert.Contains("A b", report);
        Assert.Contains("ab", report);
        vm.Options = new();
        Assert.Null(vm.Result);
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Single(vm.Alignment!.Changes);
    }
}
