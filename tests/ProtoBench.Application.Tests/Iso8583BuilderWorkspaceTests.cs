using ProtoBench.Desktop.Features.Payments;
using ProtoBench.Parsing.Definitions;

namespace ProtoBench.Application.Tests;

public sealed class Iso8583BuilderWorkspaceTests
{
    private const string Xml = """
        <isopackager>
          <isofield id="0" length="4" name="MTI" class="org.jpos.iso.IFA_NUMERIC"/>
          <isofield id="1" length="16" name="Bitmap" class="org.jpos.iso.IFA_BITMAP"/>
          <isofield id="2" length="19" name="Account" class="org.jpos.iso.IFA_LLNUM"/>
          <isofield id="3" length="6" name="Code" class="org.jpos.iso.IFA_NUMERIC"/>
        </isopackager>
        """;
    private static Iso8583BuilderWorkspace Workspace() => new(new([
        Iso8583Profile.FromXml(Xml, "ascii"),
        Iso8583Profile.FromXml(Xml.Replace("IFA_", "IFE_"), "ebcdic")], []));

    [Fact]
    public async Task AddsFieldsAndBuildsAMessageThenInvalidatesAfterEdits()
    {
        using var vm = Workspace();
        Assert.Equal("0200", Assert.Single(vm.Fields).Value);
        vm.SelectedFieldId = 2; vm.AddField();
        var account = vm.Fields.Last(); account.Value = "12345";
        Assert.DoesNotContain(vm.AvailableFields, f => f.Id == 2);
        vm.SelectedFieldId = 2; vm.AddField();
        Assert.Equal(2, vm.Fields.Count);
        await vm.BuildCommand.ExecuteAsync(null);
        Assert.Empty(vm.Error);
        Assert.Equal("020040000000000000000512345", vm.AsciiPreview);
        Assert.Equal(3, vm.PreviewFields.Count);
        Assert.True(vm.CanCopyAscii);
        account.Value = "123";
        Assert.Empty(vm.Output);
        Assert.Empty(vm.PreviewFields);
        Assert.False(vm.CanCopyAscii);
        await vm.BuildCommand.ExecuteAsync(null);
        Assert.EndsWith("03123", vm.AsciiPreview);
        vm.RemoveField(account);
        await vm.BuildCommand.ExecuteAsync(null);
        var built = vm.Output;
        account.Value = "removed field is detached";
        Assert.Equal(built, vm.Output);
        vm.RemoveField(vm.Fields[0]);
        Assert.Single(vm.Fields);
    }

    [Fact]
    public async Task ChangingProfileAndClearingResetsFieldsWithoutKeepingPreview()
    {
        using var vm = Workspace();
        await vm.BuildCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.Output);
        vm.ProfileIndex = 1;
        Assert.Empty(vm.Output);
        Assert.Single(vm.Fields);
        vm.EbcdicCodePage = 1;
        await vm.BuildCommand.ExecuteAsync(null);
        Assert.StartsWith("F0F2F0F0", vm.Output);
        Assert.False(vm.CanCopyAscii);
        vm.ClearCommand.Execute(null);
        Assert.Empty(vm.Output);
        Assert.Equal("0200", Assert.Single(vm.Fields).Value);
        Assert.Equal(1, vm.ProfileIndex);
    }

    [Fact]
    public async Task InvalidHexShowsFieldErrorAndLeavesNoStalePreview()
    {
        using var vm = Workspace();
        await vm.BuildCommand.ExecuteAsync(null);
        vm.SelectedFieldId = 3; vm.AddField();
        vm.Fields.Last().Mode = 1;
        vm.Fields.Last().Value = "XYZ";
        await vm.BuildCommand.ExecuteAsync(null);
        Assert.Contains("Field 3", vm.Error);
        Assert.Empty(vm.Output);
        Assert.Empty(vm.PreviewFields);
    }

    [Fact]
    public async Task MissingProfileIsExplained()
    {
        using var vm = new Iso8583BuilderWorkspace(new([], []));
        await vm.BuildCommand.ExecuteAsync(null);
        Assert.Contains("profile", vm.Error);
        Assert.Empty(vm.Output);
    }

    [Fact]
    public async Task BcdHexDigitCountIsValidatedAndTogglingModeClearsPreview()
    {
        var profile = Iso8583Profile.FromXml(Xml.Replace("IFA_LLNUM", "IFB_LLNUM"), "bcd");
        using var vm = new Iso8583BuilderWorkspace(new([profile], []));
        vm.SelectedFieldId = 2; vm.AddField();
        var row = vm.Fields.Last(); row.Mode = 1; row.Value = "123450"; row.DigitCount = "bad";
        Assert.True(row.ShowDigitCount);
        await vm.BuildCommand.ExecuteAsync(null);
        Assert.Contains("digit count", vm.Error);
        row.DigitCount = "5";
        await vm.BuildCommand.ExecuteAsync(null);
        Assert.Empty(vm.Error);
        Assert.Equal("12345", vm.PreviewFields.Last().Value);
        row.Mode = 0;
        Assert.Empty(vm.Output);
        Assert.False(row.ShowDigitCount);
    }
}
