using ProtoBench.Core.Documents;
using ProtoBench.Core.Fields;

namespace ProtoBench.Parsing.Tests;

public sealed class MessageComparisonTests
{
    private static ParsedField Field(string name, string value, params ParsedField[] children) => new(name, value, new(0, 0, 0, 0), children);
    private static ParsedRecord Record(params ParsedField[] fields) => new(1, "", fields);

    [Fact]
    public void MatchesSpecIdsDespiteDifferentNamesAndOrder()
    {
        var a = Record(Field("Amount", "100") with { DataElement = "4" }, Field("Trace", "001"));
        var b = Record(Field("Trace", "001"), Field("Transaction amount", "100") with { DataElement = "4" });
        var rows = MessageComparison.Compare(a, b);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(FieldComparisonStatus.Match, r.Status));
    }

    [Fact]
    public void ComparesNestedFieldsWithinTheirParentsAndPropagatesChanges()
    {
        var a = Record(Field("55", "same", Field("template", "same", Field("9F02", "100"))), Field("other", "", Field("9F02", "200")));
        var b = Record(Field("55", "same", Field("template", "same", Field("9F02", "101"))), Field("other", "", Field("9F02", "200")));
        var rows = MessageComparison.Compare(a, b);
        Assert.All(rows.Take(3), r => Assert.Equal(FieldComparisonStatus.Changed, r.Status));
        Assert.Equal("55 / template / 9F02", rows[2].Path);
        Assert.All(rows.Skip(3), r => Assert.Equal(FieldComparisonStatus.Match, r.Status));
    }

    [Fact]
    public void PreservesRepeatedFieldsAndDistinguishesMissingFromEmpty()
    {
        var a = Record(Field("tag", "one"), Field("tag", "two"), Field("empty", ""));
        var b = Record(Field("tag", "one"), Field("tag", "changed"), Field("extra", "", Field("child", "x")));
        var rows = MessageComparison.Compare(a, b);
        Assert.Equal("tag [2]", rows[1].Path);
        Assert.Equal(FieldComparisonStatus.Changed, rows[1].Status);
        Assert.Equal(FieldComparisonStatus.MissingFromMessage2, rows[2].Status);
        Assert.Equal("", rows[2].Message1);
        Assert.Null(rows[2].Message2);
        Assert.All(rows.Skip(3), r => Assert.Equal(FieldComparisonStatus.MissingFromMessage1, r.Status));
    }

    [Theory]
    [InlineData("ABC", "abc")]
    [InlineData("ABC", "ABC ")]
    [InlineData("001", "1")]
    public void UsesExactValues(string a, string b) => Assert.Equal(FieldComparisonStatus.Changed,
        Assert.Single(MessageComparison.Compare(Record(Field("x", a)), Record(Field("x", b)))).Status);
}
