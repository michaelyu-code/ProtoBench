using ProtoBench.Desktop.Features.Payments;
using ProtoBench.Infrastructure.Configuration;
using ProtoBench.Parsing.Definitions;

namespace ProtoBench.Application.Tests;

public sealed class Iso8583WorkspaceTests
{
    private const string Xml = """
        <isopackager>
          <isofield id="0" length="4" name="MTI" class="org.jpos.iso.IFA_NUMERIC"/>
          <isofield id="1" length="16" name="Bitmap" class="org.jpos.iso.IFA_BITMAP"/>
          <isofield id="3" length="6" name="Processing" class="org.jpos.iso.IFA_NUMERIC"/>
        </isopackager>
        """;

    [Fact]
    public async Task PresentsIncompleteFieldsAndClearsStaleResults()
    {
        using var vm = new Iso8583Workspace(new([Iso8583Profile.FromXml(Xml, "test")], []));
        vm.Input = "02002000000000000000123";
        await vm.ParseCommand.ExecuteAsync(null);
        Assert.Contains("Incomplete message", vm.Notice);
        Assert.Contains("Value: 123", vm.Output);
        Assert.Contains("313233", vm.Output);
        Assert.Equal(3, vm.Fields.Count);
        Assert.Equal("0200", vm.DisplayFields[0].SourceValue);
        Assert.Equal("123", vm.DisplayFields[2].SourceValue);
        vm.InputMode = 1;
        Assert.Empty(vm.Output);
        Assert.Empty(vm.Fields);
        Assert.Empty(vm.DisplayFields);
        vm.Input = "30 32 30 30 32 30 30 30 30 30 30 30 30 30 30 30 30 30 30 30 30 30 30 30 30 30";
        await vm.ParseCommand.ExecuteAsync(null);
        Assert.Equal("Complete message parsed.", vm.Notice);
        vm.ClearCommand.Execute(null);
        Assert.Empty(vm.Input);
        Assert.Empty(vm.Output);
    }

    [Theory]
    [InlineData("020040000000000000000512345", "05", "12345", "0512345")]
    [InlineData("0200400000000000000005123", "05", "123", "05123")]
    [InlineData("020040000000000000000", "0", "", "0")]
    public async Task SourceSeparatesLengthPrefixFromAvailableValue(string input, string prefix, string value, string field)
    {
        var xml = Xml.Replace("</isopackager>", "<isofield id=\"2\" length=\"19\" name=\"Account\" class=\"org.jpos.iso.IFA_LLNUM\"/></isopackager>");
        using var vm = new Iso8583Workspace(new([Iso8583Profile.FromXml(xml, "test")], []));
        vm.Input = input;
        await vm.ParseCommand.ExecuteAsync(null);
        var row = vm.DisplayFields.Last();
        Assert.Equal(prefix, row.LengthPrefix);
        Assert.Equal(value, row.SourceValue);
        Assert.Equal(field, row.SourceField);
        Assert.Equal(20 + prefix.Length, row.Field.ValueOffset);
        Assert.Equal(value.Length, row.Field.ValueByteLength);
    }

    [Fact]
    public async Task HexSourcePreservesCaseAndWhitespaceWithoutConvertingToText()
    {
        var xml = Xml.Replace("length=\"6\" name=\"Processing\" class=\"org.jpos.iso.IFA_NUMERIC\"", "length=\"2\" name=\"Processing\" class=\"org.jpos.iso.IFB_BINARY\"");
        using var vm = new Iso8583Workspace(new([Iso8583Profile.FromXml(xml, "test")], []));
        vm.InputMode = 1;
        vm.Input = "30 32 30 30 32 30 30 30 30 30 30 30 30 30 30 30 30 30 30 30 ab\nCd";
        await vm.ParseCommand.ExecuteAsync(null);
        Assert.Equal("Complete message parsed.", vm.Notice);
        Assert.Equal("30 32 30 30", vm.DisplayFields[0].SourceValue);
        Assert.Equal("ABCD", vm.DisplayFields.Last().Field.Value);
        Assert.Equal("ab\nCd", vm.DisplayFields.Last().SourceValue);
    }

    [Fact]
    public async Task RequiresProfileAndRejectsInvalidHex()
    {
        using var vm = new Iso8583Workspace(new([], []));
        await vm.ParseCommand.ExecuteAsync(null);
        Assert.Contains("packager", vm.Notice);
        vm.Profiles.Add(Iso8583Profile.FromXml(Xml, "test"));
        vm.InputMode = 1;
        vm.Input = "XYZ";
        await vm.ParseCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.Notice);
        Assert.Empty(vm.Output);
    }

    [Fact]
    public async Task Cp500ChoiceChangesDecodedValuesAndPreservesSourceHex()
    {
        var xml = Xml.Replace("IFA_NUMERIC", "IFE_NUMERIC").Replace("IFA_BITMAP", "IFE_BITMAP")
            .Replace("length=\"6\" name=\"Processing\" class=\"org.jpos.iso.IFE_NUMERIC\"", "length=\"5\" name=\"Processing\" class=\"org.jpos.iso.IFE_CHAR\"");
        using var vm = new Iso8583Workspace(new([Iso8583Profile.FromXml(xml, "cp500")], []));
        vm.InputMode = 1;
        vm.EbcdicCodePage = 1;
        vm.Input = "f0 f2 f0 f0 f2 f0 f0 f0 f0 f0 f0 f0 f0 f0 f0 f0 f0 f0 f0 f0 4a 5a 5f 4f bb";
        await vm.ParseCommand.ExecuteAsync(null);
        Assert.Equal("Complete message parsed.", vm.Notice);
        Assert.Equal("0200", vm.Fields[0].Value);
        Assert.Equal("[]^!|", vm.Fields.Last().Value);
        Assert.Equal("4a 5a 5f 4f bb", vm.DisplayFields.Last().SourceValue);
        vm.EbcdicCodePage = 0;
        Assert.Empty(vm.Fields);
        Assert.Empty(vm.Output);
        await vm.ParseCommand.ExecuteAsync(null);
        Assert.NotEqual("[]^!|", vm.Fields.Last().Value);
        Assert.Equal("4a 5a 5f 4f bb", vm.DisplayFields.Last().SourceValue);
    }

    [Fact]
    public void ProfileLoadingIsolatesBadFilesAndReportsDuplicates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "protobench-iso-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "good.xml"), Xml);
            File.WriteAllText(Path.Combine(directory, "bad.xml"), "<invalid");
            var result = Iso8583Profiles.Load([directory, directory]);
            Assert.Single(result.Definitions);
            Assert.Contains(result.Diagnostics, d => d.Contains("bad.xml"));
            Assert.Contains(result.Diagnostics, d => d.Contains("duplicate"));
        }
        finally { Directory.Delete(directory, true); }
    }
}
